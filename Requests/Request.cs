using Requests.Options;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace Requests
{

    /// <summary>
    /// A <see cref="Request{TOptions, TCompleated, TFailed}"/> object that can be managed by the <see cref="RequestHandler"/>.
    /// </summary>
    /// <typeparam name="TOptions">Type of options</typeparam>
    /// <typeparam name="TCompleated">Type of compleated return</typeparam>
    /// <typeparam name="TFailed">Type of failed return</typeparam>
    public abstract class Request<TOptions, TCompleated, TFailed> : IRequest where TOptions : RequestOptions<TCompleated, TFailed>, new()
    {
        /// <summary>
        /// If this object is disposed of.
        /// </summary>
        private bool _disposed;

        /// <summary>
        /// How often this <see cref="Request{TOptions, TCompleated, TFailed}"/> failded.
        /// </summary>
        public virtual int AttemptCounter { get; private set; }

        /// <summary>
        /// The <see cref="CancellationTokenSource"/> for this object.
        /// </summary>
        private CancellationTokenSource _cts;

        /// <summary>
        /// The <see cref="CancellationTokenRegistration"/> for this object.
        /// </summary>
        private CancellationTokenRegistration _ctr;

        /// <summary>
        /// The synchronization context captured upon construction.  This will never be null.
        /// </summary>
        protected SynchronizationContext SynchronizationContext { get; }

        /// <summary>
        /// The <see cref="RequestState"/> of this <see cref="Request{TOptions, TCompleated, TFailed}"/>.
        /// </summary>
        private RequestState _state = RequestState.Paused;

        /// <summary>
        /// The <see cref="RequestOptions{TCompleated, TFailed}"/> that started this request.
        /// </summary>
        private readonly TOptions _startOptions;

        /// <summary>
        /// <see cref="System.Threading.Tasks.Task"/> that indicates of this <see cref="Request{TOptions, TCompleated, TFailed}"/> finished.
        /// </summary>
        private readonly TaskCompletionSource _isFinished = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        /// List of all collected exceptions.
        /// </summary>
        private readonly List<Exception> _exceptions = new();

        /// <summary>
        /// The <see cref="RequestOptions{TCompleated, TFailed}"/> of this object.
        /// </summary>
        protected TOptions Options { get; set; }

        /// <summary>
        /// The <see cref="RequestOptions{TCompleated, TFailed}"/> that started this request.
        /// </summary>
        public TOptions StartOptions => _startOptions;

        /// <summary>
        /// <see cref="CancellationToken"/> that indicates if this <see cref="Request{TOptions, TCompleated, TFailed}"/> was cancelled.
        /// </summary>
        protected CancellationToken Token => _cts.Token;

        /// <summary>
        /// <see cref="System.Threading.Tasks.Task"/> that indicates of this <see cref="Request{TOptions, TCompleated, TFailed}"/> finished.
        /// </summary>
        public virtual Task Task => _isFinished.Task;

        /// <summary>
        /// <see cref="AggregateException"/> that contains the throwed Exeptions
        /// </summary>
        public virtual AggregateException? Exception { private set; get; }

        /// <summary>
        /// Delays the start of the <see cref="Request{TOptions, TCompleated, TFailed}"/> on every Start call for the specified number of milliseconds.
        /// </summary>
        public TimeSpan? DeployDelay
        {
            get => Options.DeployDelay; set => Options.DeployDelay = value;
        }

        /// <summary>
        /// The <see cref="RequestState"/> of this <see cref="Request{TOptions, TCompleated, TFailed}"/>.
        /// </summary>
        public virtual RequestState State
        {
            get => _state; protected set
            {
                if (_state is RequestState.Compleated or RequestState.Failed or RequestState.Cancelled)
                    return;
                _state = value;
                SynchronizationContext.Post((o) => StateChanged?.Invoke((IRequest)o!, value), this);
            }
        }

        /// <summary>
        /// Event that will be invoked when the <see cref="State"/> of this object changed.
        /// </summary>
        public event EventHandler<RequestState>? StateChanged;

        /// <summary>
        /// If the <see cref="Request{TOptions, TCompleated, TFailed}"/> has priority over other not prioritized <see cref="Request{TOptions, TCompleated, TFailed}">Requests</see>.
        /// </summary>
        public virtual RequestPriority Priority => Options.Priority;

        /// <summary>
        /// Consructor of the <see cref="Request{TOptions, TCompleated, TFailed}"/> class 
        /// </summary>
        /// <param name="options">Options to modify the <see cref="Request{TOptions, TCompleated, TFailed}"/></param>
        protected Request(TOptions? options = null)
        {
            _startOptions = options ?? new();
            Options = _startOptions with { };
            SynchronizationContext = SynchronizationContext.Current ?? Options.Handler.DefaultSynchronizationContext;
            RegisterNewCTS();
        }

        /// <summary>
        /// Releases all Recouces of <see cref="_ctr"/> and <see cref="_cts"/> and sets them new 
        /// </summary>
        [MemberNotNull(nameof(_cts))]
        private void RegisterNewCTS()
        {
            _cts?.Dispose();
            _ctr.Unregister();
            _cts = CreateNewCTS();
            _ctr = Token.Register(() => SynchronizationContext.Post((o) => Options.RequestCancelled?.Invoke((IRequest)o!), this));
        }

        /// <summary>
        /// Creates an new Linked Cancelation Token Source
        /// </summary>
        /// <returns></returns>
        private CancellationTokenSource CreateNewCTS()
        {
            if (Options.CancellationToken.HasValue)
                return CancellationTokenSource.CreateLinkedTokenSource(Options.Handler.CT, Options.CancellationToken.Value);
            return CancellationTokenSource.CreateLinkedTokenSource(Options.Handler.CT);
        }

        /// <summary>
        /// Cancel the <see cref="Request{TOptions, TCompleated, TFailed}"/>
        /// </summary>
        /// /// <exception cref="AggregateException"></exception>
        /// /// <exception cref="ObjectDisposedException"></exception>
        /// /// <exception cref="InvalidOperationException"></exception>
        public virtual void Cancel()
        {
            if (State == RequestState.Cancelled)
                return;
            State = RequestState.Cancelled;
            if (!_disposed)
                _cts.Cancel();
            _isFinished.TrySetCanceled();
        }

        /// <summary>
        /// Wait to finish this <see cref="IRequest"/>.
        /// </summary>
        /// <exception cref="AggregateException"></exception>
        /// <exception cref="ObjectDisposedException"></exception>
        public virtual void Wait() => Task.Wait();

        /// <summary>
        /// Dispose the <see cref="Request{TOptions, TCompleated, TFailed}"/>. 
        /// Will be called automaticly by the <see cref="RequestHandler"/>.
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

            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// If AutoStart is set Start request
        /// </summary>
        protected void AutoStart()
        {
            if (Options.AutoStart) Start();
        }

        /// <summary>
        /// Runs the <see cref="Request{TOptions, TCompleated, TFailed}"/> that was created out this object
        /// </summary>
        async Task IRequest.StartRequestAsync()
        {
            if (State != RequestState.Idle || Options.Handler.CT.IsCancellationRequested || Options.CancellationToken?.IsCancellationRequested == true)
                return;
            State = RequestState.Running;

            if (_cts.IsCancellationRequested)
                RegisterNewCTS();

            SynchronizationContext.Post((o) => Options.RequestStarted?.Invoke((IRequest)o!), this);

            RequestReturn returnItem = await TryRunRequestAsync();
            SetResult(returnItem);
        }

        private async Task<Request<TOptions, TCompleated, TFailed>.RequestReturn> TryRunRequestAsync()
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
        /// Sets the result of the Request and handles if it doesn't succeed
        /// </summary>
        /// <param name="returnItem"></param>
        private void SetResult(RequestReturn returnItem)
        {
            EvalueateRequest(returnItem);
            SetTaskState();
        }

        private void EvalueateRequest(Request<TOptions, TCompleated, TFailed>.RequestReturn returnItem)
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
        /// Sets the Status of the Task
        /// </summary>
        private void SetTaskState()
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
        /// Adds a <see cref="Exception"/> to the <see cref="Exception"/> trace
        /// </summary>
        protected void AddException(Exception exception)
        {
            _exceptions.Add(exception);
            Exception = new AggregateException(_exceptions);
        }

        /// <summary>
        /// Handles the <see cref="Request{TOptions, TCompleated, TFailed}"/> that the <see cref="HttpClient"/> should start.
        /// </summary>
        /// <returns>A <see cref="RequestReturn"/> object that indicates if the <see cref="Request{TOptions, TCompleated, TFailed}"/> was succesful and returns the return objects.</returns>
        protected abstract Task<RequestReturn> RunRequestAsync();

        /// <summary>
        /// Start the <see cref="Request{TOptions, TCompleated, TFailed}"/> if it is not yet started or paused.
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
        /// Waits that the timespan ends to deploy the <see cref="Request{TOptions, TCompleated, TFailed}"/>
        /// </summary>
        /// <param name="timeSpan">Time span to the deploy</param>
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
        /// Set the <see cref="Request{TOptions, TCompleated, TFailed}"/> on hold.
        /// </summary>
        public virtual void Pause() => State = RequestState.Paused;

        /// <summary>
        /// Class that holds the return and notification objects.
        /// </summary>
        protected class RequestReturn
        {
            /// <summary>
            /// Main constructor
            /// </summary>
            public RequestReturn() { }

            /// <summary>
            /// Object that will be returned by the <see cref="RequestOptions{TCompleated,TFailed}.RequestCompleated"/> delegate.
            /// </summary>
            public TCompleated? CompleatedReturn { get; set; }

            /// <summary>
            /// Object that will be returned by the <see cref="RequestOptions{TCompleated,TFailed}.RequestFailed"/> delegate.
            /// </summary>
            public TFailed? FailedReturn { get; set; }

            /// <summary>
            /// Indicates if the <see cref="Request{TOptions, TCompleated, TFailed}"/> was successful.
            /// </summary>
            public bool Successful { get; set; }
        }
    }
}
