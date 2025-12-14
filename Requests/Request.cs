using Requests.Options;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Requests
{
    /// <summary>
    /// Base class for requests that can be managed by the <see cref="RequestHandler"/>.
    /// </summary>
    /// <typeparam name="TOptions">The type of options.</typeparam>
    /// <typeparam name="TCompleted">The type of completed return.</typeparam>
    /// <typeparam name="TFailed">The type of failed return.</typeparam>
    public abstract class Request<TOptions, TCompleted, TFailed> : IRequest
        where TOptions : RequestOptions<TCompleted, TFailed>, new()
    {
        private bool _disposed;
        private RequestState _state = RequestState.Paused;
        private readonly TaskCompletionSource _completionSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private TaskCompletionSource _runningCompletionSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly List<Exception> _exceptions = [];
        private readonly SemaphoreSlim _pauseSemaphore = new(0, 1); // Starts locked
        private CancellationTokenSource _requestCts = null!; // Initialized in constructor
        private CancellationTokenRegistration _ctr;

        /// <summary>
        /// The synchronization context captured upon construction for marshaling callbacks.
        /// </summary>
        protected SynchronizationContext SynchronizationContext { get; }

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
        public virtual RequestState State
        {
            get => _state;
            protected set
            {
                if (HasCompleted())
                    return;

                RequestState oldState = _state;
                _state = value;

                // Complete running task if we're no longer running
                if (oldState == RequestState.Running && value != RequestState.Running)
                {
                    _runningCompletionSource.TrySetResult();
                }

                // Marshal state change to original context
                SynchronizationContext.Post(_ => StateChanged?.Invoke(this, value), null);
            }
        }

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
            SynchronizationContext = SynchronizationContext.Current ?? Options.Handler.DefaultSynchronizationContext;

            _requestCts = CreateCancellationTokenSource();
            _ctr = RegisterCancellation();
        }

        /// <summary>
        /// Creates a cancellation token source linked to handler and optional request token.
        /// </summary>
        private CancellationTokenSource CreateCancellationTokenSource() => Options.CancellationToken.HasValue
                ? CancellationTokenSource.CreateLinkedTokenSource(Options.Handler.CancellationToken, Options.CancellationToken.Value)
                : CancellationTokenSource.CreateLinkedTokenSource(Options.Handler.CancellationToken);

        /// <summary>
        /// Registers cancellation callback.
        /// </summary>
        private CancellationTokenRegistration RegisterCancellation() => Token.UnsafeRegister(state =>
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
            if (Options.Handler.CancellationToken.IsCancellationRequested && State == RequestState.Running)
            {
                Pause();
            }
        }

        /// <summary>
        /// Cancels the request permanently. This action is irreversible.
        /// </summary>
        public virtual void Cancel()
        {
            if (State == RequestState.Cancelled)
                return;

            State = RequestState.Cancelled;

            // Release pause semaphore to prevent deadlock
            if (_pauseSemaphore.CurrentCount == 0)
                _pauseSemaphore.Release();

            if (!_disposed)
                _requestCts.Cancel();

            _completionSource.TrySetCanceled();
            _runningCompletionSource.TrySetCanceled();

            // Marshal callback to original context
            SynchronizationContext.Post(_ => Options.RequestCancelled?.Invoke(this), null);

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

            State = RequestState.Idle;
            Options.Handler.RunRequests(this);
        }

        /// <summary>
        /// Pauses the request at the next yield point.
        /// </summary>
        public virtual void Pause()
        {
            if (State != RequestState.Running)
                return;

            State = RequestState.Paused;
        }

        /// <summary>
        /// Delays deployment of the request.
        /// </summary>
        private async Task WaitAndDeployAsync(TimeSpan delay)
        {
            State = RequestState.Waiting;

            try
            {
                await Task.Delay(delay, Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (State != RequestState.Waiting)
                return;

            State = RequestState.Idle;
            Options.Handler.RunRequests(this);
        }

        /// <summary>
        /// Executes the request. Called by the handler.
        /// </summary>
        async Task IRequest.StartRequestAsync()
        {
            // Check if we're resuming a paused execution (something waiting on semaphore)
            if (State == RequestState.Idle && _pauseSemaphore.CurrentCount == 0)
            {
                // There's a paused execution waiting - create new completion source for this resume
                _runningCompletionSource = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

                // Resume it
                State = RequestState.Running;
                _pauseSemaphore.Release();

                // Wait for the resumed execution to stop running (pause or complete)
                await _runningCompletionSource.Task;
                return;
            }

            // Fresh start - reset running task for this execution
            _runningCompletionSource = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            // Start the execution in the background
            _ = ExecuteInternalAsync();

            // Wait until we're no longer running (paused or completed)
            await _runningCompletionSource.Task;
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
                await YieldAsync();

                if (!ShouldExecute())
                    return;

                State = RequestState.Running;

                // Recreate CTS if we're restarting after handler cancellation
                if (_requestCts.IsCancellationRequested && Options.CancellationToken?.IsCancellationRequested == false)
                {
                    await ResetCancellationTokenAsync();
                }

                // Marshal callback to original context
                SynchronizationContext.Post(_ => Options.RequestStarted?.Invoke(this), null);

                // Execute the request
                RequestReturn result = await ExecuteRequestAsync();

                // Process the result
                await ProcessResultAsync(result);
            }
            catch (OperationCanceledException)
            {
                // Cancellation is already handled
            }
            catch (Exception ex)
            {
                AddException(ex);
                await ProcessResultAsync(new RequestReturn { Successful = false });
            }
            finally
            {
                Request.SetCurrent(null);
            }
        }

        /// <summary>
        /// Checks if the request should execute.
        /// </summary>
        private bool ShouldExecute() =>
            State == RequestState.Idle
            && !Options.Handler.CancellationToken.IsCancellationRequested
            && Options.CancellationToken?.IsCancellationRequested != true;

        /// <summary>
        /// Resets the cancellation token source.
        /// </summary>
        private async Task ResetCancellationTokenAsync()
        {
            await _ctr.DisposeAsync();
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
                return await RunRequestAsync();
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
                State = RequestState.Completed;
                _completionSource.TrySetResult();

                if (result.CompletedReturn is not null)
                {
                    SynchronizationContext.Post(_ => Options.RequestCompleted?.Invoke(this, result.CompletedReturn), null);
                }
            }
            else
            {
                AttemptCounter++;

                // Check if request itself was cancelled
                if (Options.CancellationToken?.IsCancellationRequested == true)
                {
                    State = RequestState.Cancelled;
                    _completionSource.TrySetCanceled();
                    return;
                }

                // Check if handler was cancelled - pause and wait for resume
                if (Options.Handler.CancellationToken.IsCancellationRequested)
                {
                    State = RequestState.Idle;
                    return;
                }

                // Check if we should retry
                if (AttemptCounter < Options.NumberOfAttempts)
                {
                    if (Options.DelayBetweenAttemps.HasValue)
                    {
                        await WaitAndDeployAsync(Options.DelayBetweenAttemps.Value);
                    }
                    else
                    {
                        State = RequestState.Idle;
                        Options.Handler.RunRequests(this);
                    }
                    return;
                }

                // All retries exhausted
                State = RequestState.Failed;
                _completionSource.TrySetResult();

                if (result.FailedReturn is not null)
                {
                    SynchronizationContext.Post(_ => Options.RequestFailed?.Invoke(this, result.FailedReturn), null);
                }
            }
        }

        /// <summary>
        /// Yield point that respects pause and cancellation state.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public async Task YieldAsync()
        {
            Token.ThrowIfCancellationRequested();

            if (State == RequestState.Paused)
            {
                // Wait for handler to resume us
                await _pauseSemaphore.WaitAsync(Token);
            }
        }

        /// <summary>
        /// Adds an exception to the aggregate exception list.
        /// </summary>
        protected void AddException(Exception exception)
        {
            _exceptions.Add(exception);
            Exception = new AggregateException(_exceptions);
            SynchronizationContext.Post(_ => Options.RequestExceptionOccurred?.Invoke(this, exception), null);
        }

        /// <summary>
        /// Attempts to set the request state to idle.
        /// </summary>
        public bool TrySetIdle()
        {
            if (HasCompleted())
                return false;

            State = RequestState.Idle;
            return true;
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
        public TaskAwaiter GetRunningAwaiter() => _runningCompletionSource.Task.GetAwaiter();

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
            _pauseSemaphore?.Dispose();
            _completionSource.TrySetCanceled();
            _runningCompletionSource.TrySetCanceled();

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
            public static RequestReturn Success(TCompleted value) => new()
            {
                Successful = true,
                CompletedReturn = value
            };

            /// <summary>
            /// Creates a failed result.
            /// </summary>
            public static RequestReturn Failure(TFailed value) => new()
            {
                Successful = false,
                FailedReturn = value
            };
        }
    }
}