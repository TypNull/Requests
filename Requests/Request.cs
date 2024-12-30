using Requests.Options;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace Requests
{

    /// <summary>
    /// Represents a <see cref="Request{TOptions, TCompleated, TFailed}"/> object that can be managed by the <see cref="RequestHandler"/>.
    /// </summary>
    /// <typeparam name="TOptions">The type of options.</typeparam>
    /// <typeparam name="TCompleated">The type of completed return.</typeparam>
    /// <typeparam name="TFailed">The type of failed return.</typeparam>
    public abstract class Request<TOptions, TCompleated, TFailed> : IRequest where TOptions : RequestOptions<TCompleated, TFailed>, new()
    {
        /// <summary>
        /// Indicates whether this object has been disposed of.
        /// </summary>
        private bool _disposed;

        /// <summary>
        /// Keeps track of how many times this <see cref="Request{TOptions, TCompleated, TFailed}"/> failed.
        /// </summary>
        public virtual int AttemptCounter { get; private set; }

        /// <summary>
        /// The <see cref="CancellationTokenSource"/> associated with this object.
        /// </summary>
        private CancellationTokenSource _cts;

        /// <summary>
        /// The <see cref="CancellationTokenRegistration"/> associated with this object.
        /// </summary>
        private CancellationTokenRegistration _ctr;

        /// <summary>
        /// The synchronization context captured upon construction. This will never be null.
        /// </summary>
        protected SynchronizationContext SynchronizationContext { get; }

        /// <summary>
        /// The current state of this <see cref="Request{TOptions, TCompleated, TFailed}"/>.
        /// </summary>
        private RequestState _state = RequestState.Paused;

        /// <summary>
        /// The <see cref="RequestOptions{TCompleated, TFailed}"/> that initiated this request.
        /// </summary>
        private readonly TOptions _startOptions;

        /// <summary>
        /// A <see cref="System.Threading.Tasks.Task"/> that indicates whether this <see cref="Request{TOptions, TCompleated, TFailed}"/> has finished.
        /// </summary>
        private readonly TaskCompletionSource _isFinished = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        /// A list of all collected exceptions during the request.
        /// </summary>
        private readonly List<Exception> _exceptions = new();

        /// <summary>
        /// The <see cref="RequestOptions{TCompleated, TFailed}"/> associated with this object.
        /// </summary>
        protected TOptions Options { get; set; }

        /// <summary>
        /// The <see cref="RequestOptions{TCompleated, TFailed}"/> that started this request.
        /// </summary>
        public TOptions StartOptions => _startOptions;

        /// <summary>
        /// A <see cref="CancellationToken"/> that indicates whether this <see cref="Request{TOptions, TCompleated, TFailed}"/> was cancelled.
        /// </summary>
        protected CancellationToken Token => _cts.Token;

        /// <summary>
        /// A <see cref="System.Threading.Tasks.Task"/> that indicates whether this <see cref="Request{TOptions, TCompleated, TFailed}"/> has finished.
        /// </summary>
        public virtual Task Task => _isFinished.Task;

        /// <summary>
        /// An <see cref="AggregateException"/> that contains any thrown exceptions.
        /// </summary>
        public virtual AggregateException? Exception { private set; get; }

        /// <summary>
        /// Delays the start of the <see cref="Request{TOptions, TCompleated, TFailed}"/> on every Start call for the specified number of milliseconds. <br/>
        /// This property can not be set while initialization.
        /// </summary>
        public TimeSpan? DeployDelay
        {
            get => Options.DeployDelay;
            set => Options.DeployDelay = value;
        }

        /// <summary>
        /// The current state of this <see cref="Request{TOptions, TCompleated, TFailed}"/>.
        /// </summary>
        public virtual RequestState State
        {
            get => _state; protected set
            {
                if (HasCompleted())
                    return;
                _state = value;
                SynchronizationContext.Post((o) => StateChanged?.Invoke((IRequest)o!, value), this);
            }
        }

        /// <summary>
        /// Event that is invoked when the <see cref="State"/> of this object changes.
        /// </summary>
        public event EventHandler<RequestState>? StateChanged;

        /// <summary>
        /// Gets the priority of the <see cref="Request{TOptions, TCompleted, TFailed}"/>.
        /// </summary>
        public virtual RequestPriority Priority => Options.Priority;

        /// <inheritdoc/>
        IRequest? IRequest.SubsequentRequest => Options.SubsequentRequest;

        /// <summary>
        /// Constructor for the <see cref="Request{TOptions, TCompleted, TFailed}"/> class.
        /// </summary>
        /// <param name="options">Options to modify the <see cref="Request{TOptions, TCompleted, TFailed}"/>.</param>
        protected Request(TOptions? options = null)
        {
            _startOptions = options ?? new();
            Options = _startOptions with { };
            SynchronizationContext = SynchronizationContext.Current ?? Options.Handler.DefaultSynchronizationContext;
            RegisterNewCTS();
        }

        /// <summary>
        /// Releases all resources associated with <see cref="_ctr"/> and <see cref="_cts"/> and sets them anew.
        /// </summary>
        [MemberNotNull(nameof(_cts))]
        private void RegisterNewCTS()
        {
            _cts?.Dispose();
            _ctr.Unregister();
            _cts = CreateNewCTS();
            _ctr = Token.Register(() => { Cancel(); SynchronizationContext.Post((o) => Options.RequestCancelled?.Invoke((IRequest)o!), this); });
        }

        /// <summary>
        /// Creates a new linked cancellation token source.
        /// </summary>
        /// <returns>A new <see cref="CancellationTokenSource"/>.</returns>
        private CancellationTokenSource CreateNewCTS()
        {
            if (Options.CancellationToken.HasValue)
                return CancellationTokenSource.CreateLinkedTokenSource(Options.Handler.CancellationToken, Options.CancellationToken.Value);
            return CancellationTokenSource.CreateLinkedTokenSource(Options.Handler.CancellationToken);
        }

        /// <summary>
        /// Cancels the <see cref="Request{TOptions, TCompleted, TFailed}"/>.
        /// </summary>
        /// <exception cref="AggregateException"></exception>
        /// <exception cref="ObjectDisposedException"></exception>
        /// <exception cref="InvalidOperationException"></exception>
        public virtual void Cancel()
        {
            if (State == RequestState.Cancelled)
                return;
            State = RequestState.Cancelled;
            if (!_disposed)
                _cts.Cancel();
            _isFinished.TrySetCanceled();
            Options.SubsequentRequest?.Cancel();
        }

        /// <summary>
        /// Waits for this <see cref="IRequest"/> to finish.
        /// </summary>
        /// <exception cref="AggregateException"></exception>
        /// <exception cref="ObjectDisposedException"></exception>
        public virtual void Wait() => Task.Wait();

        /// <summary>
        /// Disposes the <see cref="Request{TOptions, TCompleted, TFailed}"/>.
        /// This method is automatically called by the <see cref="RequestHandler"/>.
        /// </summary>
        /// <exception cref="AggregateException"></exception>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="ObjectDisposedException"></exception>
        /// <exception cref="InvalidOperationException"></exception>
        public virtual void Dispose()
        {
            if (State == RequestState.Running)
                Cancel();
            if (_disposed)
                return;
            _disposed = true;

            _cts?.Dispose();
            _ctr.Unregister();
            _isFinished.TrySetCanceled();
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// If AutoStart is set, starts the request.
        /// </summary>
        protected void AutoStart()
        {
            if (Options.AutoStart) Start();
        }

        /// <summary>
        /// Executes the request that was instantiated from this class.
        /// </summary>
        async Task IRequest.StartRequestAsync()
        {
            if (State != RequestState.Idle || Options.Handler.CancellationToken.IsCancellationRequested || Options.CancellationToken?.IsCancellationRequested == true)
                return;
            State = RequestState.Running;

            if (_cts.IsCancellationRequested)
                RegisterNewCTS();

            SynchronizationContext.Post((o) => Options.RequestStarted?.Invoke((IRequest)o!), this);

            RequestReturn returnItem = await TryRunRequestAsync();
            SetResult(returnItem);
        }

        private async Task<RequestReturn> TryRunRequestAsync()
        {
            RequestReturn returnItem = new();
            try
            {
                returnItem = await RunRequestAsync();
            }
            catch (Exception ex)
            {
                AddException(ex);
                Debug.Assert(false, ex.Message);
            }
            return returnItem;
        }

        /// <summary>
        /// Evaluates the result of the <see cref="Request{TOptions, TCompleated, TFailed}"/> and manages the outcome if it's not successful.
        /// </summary>
        /// <param name="returnItem"> object that indicates the success of the <see cref="Request{TOptions, TCompleated, TFailed}"/></param>
        private void SetResult(RequestReturn returnItem)
        {
            EvalueateRequest(returnItem);
            SetTaskState();
        }

        private void EvalueateRequest(RequestReturn returnItem)
        {
            if (State != RequestState.Running)
                return;
            if (!returnItem.Successful)
            {
                if (Token.IsCancellationRequested || AttemptCounter++ < Options.NumberOfAttempts)
                {
                    if (Options.CancellationToken?.IsCancellationRequested == true)
                        State = RequestState.Cancelled;
                    else if (Options.DelayBetweenAttemps.HasValue)
                        _ = WaitAndDeploy(Options.DelayBetweenAttemps.Value);
                    else
                        State = RequestState.Idle;
                    return;
                }
                State = RequestState.Failed;
                SynchronizationContext.Post((o) => Options.RequestFailed?.Invoke((IRequest)o!, returnItem.FailedReturn), this);
                return;
            }
            State = RequestState.Compleated;
            SynchronizationContext.Post((o) => Options.RequestCompleated?.Invoke((IRequest)o!, returnItem.CompleatedReturn), this);
        }


        /// <summary>
        /// Updates the Task's status based on the current state of the <see cref="Request{TOptions, TCompleated, TFailed}"/>.
        /// </summary>
        protected void SetTaskState()
        {
            switch (State)
            {
                case RequestState.Compleated:
                    _isFinished.TrySetResult();
                    break;
                case RequestState.Failed:
                    _isFinished.TrySetResult();
                    break;
                case RequestState.Cancelled:
                    _isFinished.TrySetCanceled();
                    break;
            }
        }

        /// <summary>
        /// Appends an <see cref="System.Exception"/> to the <see cref="AggregateException"/> trace.
        /// </summary>
        protected void AddException(Exception exception)
        {
            _exceptions.Add(exception);
            Exception = new AggregateException(_exceptions);
        }

        /// <summary>
        /// Contains the execution of the <see cref="Request{TOptions, TCompleated, TFailed}"/> implementation.
        /// </summary>
        /// <returns>A <see cref="RequestReturn"/> object that indicates the success of the <see cref="Request{TOptions, TCompleated, TFailed}"/> and returns the result objects.</returns>
        protected abstract Task<RequestReturn> RunRequestAsync();

        /// <summary>
        /// Starts the <see cref="Request{TOptions, TCompleated, TFailed}"/> if it hasn't started or is paused.
        /// </summary>
        public virtual void Start()
        {
            if (State != RequestState.Paused)
                return;
            if (DeployDelay.HasValue)
            {
                _ = WaitAndDeploy(DeployDelay.Value);
                return;
            }
            State = RequestState.Idle;
            Options.Handler.RunRequests(this);
        }

        /// <summary>
        /// Delays the deployment of the <see cref="Request{TOptions, TCompleated, TFailed}"/> until the specified timespan has elapsed.
        /// </summary>
        /// <param name="timeSpan">The delay duration before deploying the <see cref="Request{TOptions, TCompleated, TFailed}"/>.</param>
        private async Task WaitAndDeploy(TimeSpan timeSpan)
        {
            State = RequestState.Waiting;
            await Task.Delay(timeSpan);
            if (State != RequestState.Waiting)
                return;
            State = RequestState.Idle;
            Options.Handler.RunRequests(this);
        }

        /// <summary>
        /// Puts the <see cref="Request{TOptions, TCompleated, TFailed}"/> into a paused state.
        /// </summary>
        public virtual void Pause() => State = RequestState.Paused;

        /// <summary>
        /// Attempts to transition the <see cref="IRequest"/> state to idle.
        /// This is possible only if the request is not completed, failed, or cancelled.
        /// </summary>
        /// <returns>True if the request is in an idle <see cref="RequestState"/>, otherwise false.</returns>
        public bool TrySetIdle()
        {
            State = RequestState.Idle;
            return State == RequestState.Idle;
        }

        /// <summary>
        /// Sets a subsequent <see cref="IRequest"/> if neither the current nor the provided request has completed.
        /// </summary>
        /// <param name="request">The subsequent request to set.</param>
        /// <returns><c>true</c> if the request was set; otherwise, <c>false</c>.</returns>
        public bool TrySetSubsequentRequest(IRequest request)
        {
            if (HasCompleted() || request.HasCompleted())
                return false;
            Options.SubsequentRequest = request;
            return true;
        }

        /// <summary>
        /// Checks whether the <see cref="IRequest"/> has reached a final state (e.g., completed, failed, or cancelled) and will no longer change.
        /// </summary>
        /// <returns><c>true</c> if the request is in a final state; otherwise, <c>false</c>.</returns>
        public bool HasCompleted() => State is RequestState.Compleated or RequestState.Failed or RequestState.Cancelled;

        /// <summary>
        /// A class that encapsulates the return objects and notifications for <see cref="Request{TOptions, TCompleated, TFailed}"/>.
        /// </summary>
        protected class RequestReturn
        {
            /// <summary>
            /// Default constructor.
            /// </summary>
            public RequestReturn() { }


            /// <summary>
            /// Initializes a new instance of the <see cref="Request{TOptions, TCompleated, TFailed}.RequestReturn"/> class.
            /// </summary>
            /// <param name="successful">Indicates whether the operation was successful.</param>
            /// <param name="compleatedReturn">The object to be returned if the operation completed successfully.</param>
            /// <param name="failedReturn">The object to be returned if the operation failed.</param>
            public RequestReturn(bool successful, TCompleated compleatedReturn, TFailed failedReturn)
            {
                Successful = successful;
                CompleatedReturn = compleatedReturn;
                FailedReturn = failedReturn;
            }


            /// <summary>
            /// The object that will be returned when the <see cref="RequestOptions{TCompleated,TFailed}.RequestCompleated"/> delegate is invoked.
            /// </summary>
            public TCompleated? CompleatedReturn { get; set; }

            /// <summary>
            /// The object that will be returned when the <see cref="RequestOptions{TCompleated,TFailed}.RequestFailed"/> delegate is invoked.
            /// </summary>
            public TFailed? FailedReturn { get; set; }

            /// <summary>
            /// A flag indicating whether the <see cref="Request{TOptions, TCompleated, TFailed}"/> was successful.
            /// </summary>
            public bool Successful { get; set; }
        }
    }
}
