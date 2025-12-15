using Requests.Options;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks.Sources;

namespace Requests
{
    /// <summary>
    /// Base class for requests that can be managed by the <see cref="ParallelRequestHandler"/>.
    /// </summary>
    public abstract partial class Request<TOptions, TCompleted, TFailed> : IRequest, IValueTaskSource
        where TOptions : RequestOptions<TCompleted, TFailed>, new()
    {
        private bool _disposed;
        private readonly TaskCompletionSource _completionSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private ManualResetValueTaskSourceCore<bool> _runningSource;
        private short _runningSourceVersion;
        private readonly List<Exception> _exceptions = [];
        private CancellationTokenSource _requestCts = null!;
        private CancellationTokenRegistration _ctr;

        private TaskCompletionSource<bool>? _pauseTcs;
        private volatile bool _hasPausedExecution;

        private readonly RequestStateMachine _stateMachine;

        private static readonly SendOrPostCallback s_stateChangedCallback = static state =>
        {
            (Request<TOptions, TCompleted, TFailed> request, RequestState newState) =
                ((Request<TOptions, TCompleted, TFailed>, RequestState))state!;
            request.StateChanged?.Invoke(request, newState);
        };

        private static readonly SendOrPostCallback s_requestStartedCallback = static state =>
        {
            Request<TOptions, TCompleted, TFailed> request = (Request<TOptions, TCompleted, TFailed>)state!;
            request.Options.RequestStarted?.Invoke(request);
        };

        private static readonly SendOrPostCallback s_requestCancelledCallback = static state =>
        {
            Request<TOptions, TCompleted, TFailed> request = (Request<TOptions, TCompleted, TFailed>)state!;
            request.Options.RequestCancelled?.Invoke(request);
        };

        private static readonly SendOrPostCallback s_requestCompletedCallback = static state =>
        {
            (Request<TOptions, TCompleted, TFailed> request, TCompleted? result) =
                ((Request<TOptions, TCompleted, TFailed>, TCompleted))state!;
            request.Options.RequestCompleted?.Invoke(request, result);
        };

        private static readonly SendOrPostCallback s_requestFailedCallback = static state =>
        {
            (Request<TOptions, TCompleted, TFailed> request, TFailed? result) =
                ((Request<TOptions, TCompleted, TFailed>, TFailed))state!;
            request.Options.RequestFailed?.Invoke(request, result);
        };

        private static readonly SendOrPostCallback s_requestExceptionCallback = static state =>
        {
            (Request<TOptions, TCompleted, TFailed> request, Exception? exception) =
                ((Request<TOptions, TCompleted, TFailed>, Exception))state!;
            request.Options.RequestExceptionOccurred?.Invoke(request, exception);
        };


        /// <summary>
        /// The synchronization context captured upon construction for marshaling callbacks.
        /// </summary>
        protected SynchronizationContext? SynchronizationContext { get; }

        /// <summary>
        /// The options that started this request (immutable).
        /// </summary>
        public TOptions StartOptions { get; }

        /// <summary>
        /// The current options for this request (mutable).
        /// </summary>
        protected TOptions Options { get; set; }

        /// <summary>
        /// Number of times this request has been attempted.
        /// </summary>
        public virtual int AttemptCounter { get; private set; }

        /// <summary>
        /// Cancellation token for this request.
        /// </summary>
        protected CancellationToken Token => _requestCts.Token;

        /// <summary>
        /// Task that completes when the request reaches a final state.
        /// </summary>
        public virtual Task Task => _completionSource.Task;

        /// <summary>
        /// Aggregate exception containing all errors that occurred during execution.
        /// </summary>
        public virtual AggregateException? Exception { get; private set; }

        /// <summary>
        /// Deployment delay before the request starts execution.
        /// </summary>
        public TimeSpan? DeployDelay
        {
            get => Options.DeployDelay;
            set => Options.DeployDelay = value;
        }

        /// <summary>
        /// Current state of the request.
        /// </summary>
        public virtual RequestState State => _stateMachine.Current;

        /// <summary>
        /// Event raised when the request state changes.
        /// </summary>
        public event EventHandler<RequestState>? StateChanged;

        /// <summary>
        /// Priority of this request.
        /// </summary>
        public virtual RequestPriority Priority => Options.Priority;

        /// <inheritdoc/>
        IRequest? IRequest.SubsequentRequest => Options.SubsequentRequest;

        /// <summary>
        /// Initializes a new request with the specified options.
        /// </summary>
        protected Request(TOptions? options = null)
        {
            StartOptions = options ?? new();
            Options = StartOptions with { };
            SynchronizationContext = SynchronizationContext.Current ?? Options.Handler?.DefaultSynchronizationContext;

            // Initialize state machine
            _stateMachine = new RequestStateMachine(
                RequestState.Paused,
                OnStateChanged);

            _runningSource.RunContinuationsAsynchronously = true;

            _requestCts = CreateCancellationTokenSource();
            _ctr = RegisterCancellation();
        }


        /// <summary>
        /// Callback when state changes.
        /// </summary>
        private void OnStateChanged(RequestState oldState, RequestState newState)
        {
            // Complete running source if we're no longer running
            if (oldState == RequestState.Running && newState != RequestState.Running)
            {
                _runningSource.SetResult(true);
            }

            // Marshal state change to original context
            SynchronizationContext?.Post(s_stateChangedCallback, (this, newState));
        }

        /// <summary>
        /// Creates a cancellation token source linked to handler and optional request token.
        /// </summary>
        private CancellationTokenSource CreateCancellationTokenSource()
        {
            if (Options.Handler == null)
            {
                return Options.CancellationToken.HasValue
                    ? CancellationTokenSource.CreateLinkedTokenSource(Options.CancellationToken.Value)
                    : new CancellationTokenSource();
            }

            return Options.CancellationToken.HasValue
                ? CancellationTokenSource.CreateLinkedTokenSource(Options.Handler.CancellationToken, Options.CancellationToken.Value)
                : CancellationTokenSource.CreateLinkedTokenSource(Options.Handler.CancellationToken);
        }

        /// <summary>
        /// Registers cancellation callback.
        /// </summary>
        private CancellationTokenRegistration RegisterCancellation() => Token.UnsafeRegister(static state =>
        {
            Request<TOptions, TCompleted, TFailed> request = (Request<TOptions, TCompleted, TFailed>)state!;
            request.HandleCancellation();
        }, this);

        /// <summary>
        /// Handles cancellation based on the source.
        /// </summary>
        private void HandleCancellation()
        {
            // If request's own token was cancelled, cancel permanently
            if (Options.CancellationToken?.IsCancellationRequested == true)
            {
                Cancel();
                return;
            }

            // If handler token was cancelled, pause and wait for handler to resume
            if (Options.Handler?.CancellationToken.IsCancellationRequested == true && State == RequestState.Running)
            {
                Pause();
            }
        }

        /// <summary>
        /// Cancels the request permanently. This action is irreversible.
        /// </summary>
        public virtual void Cancel()
        {
            if (!_stateMachine.TryTransition(RequestState.Cancelled))
                return; // Already in terminal state

            // Signal any paused execution
            Interlocked.Exchange(ref _pauseTcs, null)?.TrySetCanceled();

            if (!_disposed)
                _requestCts.Cancel();

            _completionSource.TrySetCanceled();

            // Marshal callback to original context
            SynchronizationContext?.Post(s_requestCancelledCallback, this);

            Options.SubsequentRequest?.Cancel();
        }

        /// <summary>
        /// Starts or resumes the request.
        /// </summary>
        public virtual void Start()
        {
            if (State != RequestState.Paused)
                return;

            if (DeployDelay.HasValue)
            {
                _ = WaitAndDeployAsync(DeployDelay.Value);
                return;
            }

            _stateMachine.TryTransition(RequestState.Idle);
            Options.Handler?.Add(this);
        }

        /// <summary>
        /// Pauses the request at the next yield point.
        /// </summary>
        public virtual void Pause()
        {
            if (State != RequestState.Running)
                return;

            _stateMachine.TryTransition(RequestState.Paused);
        }

        /// <summary>
        /// Delays deployment of the request.
        /// </summary>
        private async Task WaitAndDeployAsync(TimeSpan delay)
        {
            _stateMachine.TryTransition(RequestState.Waiting);

            try
            {
                await Task.Delay(delay, Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (State != RequestState.Waiting)
                return;

            _stateMachine.TryTransition(RequestState.Idle);
            Options.Handler?.Add(this);
        }

        /// <summary>
        /// Executes the request. Called by the handler.
        /// </summary>
        async Task IRequest.StartRequestAsync()
        {
            // Check if we're resuming a paused execution
            if (_hasPausedExecution)
            {
                // Get and clear the pause TCS atomically
                TaskCompletionSource<bool>? tcs = Interlocked.Exchange(ref _pauseTcs, null);
                if (tcs != null)
                {
                    _hasPausedExecution = false;

                    // Create new running source for this resume
                    _runningSourceVersion++;
                    _runningSource.Reset();

                    // Resume the paused execution
                    _stateMachine.TryTransition(RequestState.Running);
                    tcs.TrySetResult(true);

                    // Wait for the resumed execution to stop running
                    await new ValueTask(this, _runningSourceVersion).ConfigureAwait(false);
                    return;
                }
            }

            // Fresh start, reset running source for this execution
            _runningSourceVersion++;
            _runningSource.Reset();

            // Start the execution in the background
#pragma warning disable CS4014
            ExecuteInternalAsync();
#pragma warning restore CS4014

            // Wait until we're no longer running
            await new ValueTask(this, _runningSourceVersion).ConfigureAwait(false);
        }

        /// <summary>
        /// Internal execution method that runs the request logic.
        /// </summary>
        private async Task ExecuteInternalAsync()
        {
            // Set this request as the current context for Request.Yield()
            Request.SetCurrent(this);

            try
            {
                // Entry checkpoint
                await YieldAsync().ConfigureAwait(false);

                if (!ShouldExecute())
                    return;

                _stateMachine.TryTransition(RequestState.Running);

                // Recreate CTS if we're restarting after handler cancellation
                if (_requestCts.IsCancellationRequested && Options.CancellationToken?.IsCancellationRequested == false)
                {
                    await ResetCancellationTokenAsync().ConfigureAwait(false);
                }

                // Marshal callback to original context
                SynchronizationContext?.Post(s_requestStartedCallback, this);

                // Execute the request
                RequestReturn result = await ExecuteRequestAsync().ConfigureAwait(false);

                // Process the result
                await ProcessResultAsync(result).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AddException(ex);
                await ProcessResultAsync(new RequestReturn { Successful = false }).ConfigureAwait(false);
            }
            finally
            {
                Request.SetCurrent(null);
            }
        }

        /// <summary>
        /// Checks if the request should execute.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool ShouldExecute() =>
            State == RequestState.Idle
            && Options.Handler?.CancellationToken.IsCancellationRequested != true
            && Options.CancellationToken?.IsCancellationRequested != true;

        /// <summary>
        /// Resets the cancellation token source.
        /// </summary>
        private async Task ResetCancellationTokenAsync()
        {
            await _ctr.DisposeAsync().ConfigureAwait(false);
            _requestCts.Dispose();
            _requestCts = CreateCancellationTokenSource();
            _ctr = RegisterCancellation();
        }

        /// <summary>
        /// Executes the request with exception handling.
        /// </summary>
        private async Task<RequestReturn> ExecuteRequestAsync()
        {
            try
            {
                return await RunRequestAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AddException(ex);
                Debug.WriteLine($"Request exception on attempt {AttemptCounter + 1}: {ex.Message}");
                return new RequestReturn { Successful = false };
            }
        }

        /// <summary>
        /// Processes the result of the request execution.
        /// </summary>
        private async Task ProcessResultAsync(RequestReturn result)
        {
            // If state changed (e.g., paused manually), just return
            if (State != RequestState.Running)
                return;

            if (result.Successful)
            {
                _stateMachine.TryTransition(RequestState.Completed);
                _completionSource.TrySetResult();

                if (result.CompletedReturn is not null)
                {
                    SynchronizationContext?.Post(s_requestCompletedCallback, (this, result.CompletedReturn));
                }
            }
            else
            {
                AttemptCounter++;

                // Check if request itself was cancelled
                if (Options.CancellationToken?.IsCancellationRequested == true)
                {
                    _stateMachine.TryTransition(RequestState.Cancelled);
                    _completionSource.TrySetCanceled();
                    return;
                }

                // Check if handler was cancelled, pause and wait for resume
                if (Options.Handler?.CancellationToken.IsCancellationRequested == true)
                {
                    _stateMachine.TryTransition(RequestState.Paused); // TODO: Not clear if Paused or Idle deciding later
                    return;
                }

                // Check if we should retry
                if (AttemptCounter < Options.NumberOfAttempts)
                {
                    if (Options.DelayBetweenAttemps.HasValue)
                    {
                        await WaitAndDeployAsync(Options.DelayBetweenAttemps.Value).ConfigureAwait(false);
                    }
                    else
                    {
                        _stateMachine.TryTransition(RequestState.Idle);
                        Options.Handler?.Add(this);
                    }
                    return;
                }

                // All retries exhausted
                _stateMachine.TryTransition(RequestState.Failed);
                _completionSource.TrySetResult();

                if (result.FailedReturn is not null)
                {
                    SynchronizationContext?.Post(s_requestFailedCallback, (this, result.FailedReturn));
                }
            }
        }

        /// <summary>
        /// Yield point that respects pause and cancellation state.
        /// Optimized for the common case where no pause/cancellation occurs.
        /// Compatible with static Request.Yield() pattern.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ValueTask YieldAsync()
        {
            // Fast path: no cancellation, not paused
            if (!Token.IsCancellationRequested && State != RequestState.Paused)
                return ValueTask.CompletedTask;

            // Slow path: need to check cancellation or wait on pause
            return YieldAsyncSlow();
        }

        /// <summary>
        /// Slow path for YieldAsync when pause or cancellation is involved.
        /// </summary>
        private async ValueTask YieldAsyncSlow()
        {
            Token.ThrowIfCancellationRequested();

            if (State == RequestState.Paused)
            {
                // Lazy-create pause TCS only when actually pausing
                TaskCompletionSource<bool> tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
                _pauseTcs = tcs;
                _hasPausedExecution = true;

                // Wait for resume
                await tcs.Task.ConfigureAwait(false);

                // We've been resumed, prepare new running source
                _runningSourceVersion++;
                _runningSource.Reset();
            }
        }

        /// <summary>
        /// Adds an exception to the aggregate exception list.
        /// </summary>
        protected void AddException(Exception exception)
        {
            _exceptions.Add(exception);
            Exception = new AggregateException(_exceptions);
            SynchronizationContext?.Post(s_requestExceptionCallback, (this, exception));
        }

        /// <summary>
        /// Attempts to set the request state to idle.
        /// </summary>
        public bool TrySetIdle()
        {
            if (HasCompleted())
                return false;

            return _stateMachine.TryTransition(RequestState.Idle);
        }

        /// <summary>
        /// Sets a subsequent request to execute after this one completes.
        /// </summary>
        public bool TrySetSubsequentRequest(IRequest request)
        {
            if (HasCompleted() || request.HasCompleted())
                return false;

            Options.SubsequentRequest = request;
            return true;
        }

        /// <summary>
        /// Checks if the request has reached a final state.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasCompleted() => State is RequestState.Completed or RequestState.Failed or RequestState.Cancelled;

        /// <summary>
        /// Waits for the request to complete.
        /// </summary>
        public virtual void Wait() => Task.Wait();

        /// <summary>
        /// Gets an awaiter for this request (equivalent to awaiting Task).
        /// </summary>
        public TaskAwaiter GetAwaiter() => Task.GetAwaiter();

        /// <summary>
        /// Gets an awaiter that completes when the request is no longer running.
        /// Useful for waiting until a request is paused or completed.
        /// </summary>
        public ValueTask GetRunningAwaiter() => new(this, _runningSourceVersion);

        /// <summary>
        /// Disposes the request and releases all resources.
        /// </summary>
        public virtual void Dispose()
        {
            if (_disposed)
                return;

            if (!HasCompleted())
                Cancel();

            _disposed = true;

            _requestCts?.Dispose();
            _ctr.Dispose();
            _completionSource.TrySetCanceled();
            _pauseTcs?.TrySetCanceled();

            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Starts the request if auto-start is enabled.
        /// </summary>
        protected void AutoStart()
        {
            if (Options.AutoStart)
                Start();
        }

        /// <summary>
        /// Abstract method to be implemented by derived classes.
        /// Contains the core logic of the request.
        /// </summary>
        protected abstract Task<RequestReturn> RunRequestAsync();

        ValueTaskSourceStatus IValueTaskSource.GetStatus(short token) => _runningSource.GetStatus(token);

        void IValueTaskSource.OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags) =>
            _runningSource.OnCompleted(continuation, state, token, flags);

        void IValueTaskSource.GetResult(short token) => _runningSource.GetResult(token);

        /// <summary>
        /// Encapsulates the return value and status of a request execution.
        /// </summary>
        protected class RequestReturn
        {
            /// <summary>
            /// Indicates whether the request completed successfully.
            /// </summary>
            public bool Successful { get; set; }

            /// <summary>
            /// The value returned on successful completion.
            /// </summary>
            public TCompleted? CompletedReturn { get; set; }

            /// <summary>
            /// The value returned on failure.
            /// </summary>
            public TFailed? FailedReturn { get; set; }

            /// <summary>
            /// Creates a successful result.
            /// </summary>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static RequestReturn Success(TCompleted value) => new()
            {
                Successful = true,
                CompletedReturn = value
            };

            /// <summary>
            /// Creates a failed result.
            /// </summary>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static RequestReturn Failure(TFailed value) => new()
            {
                Successful = false,
                FailedReturn = value
            };
        }
    }
}