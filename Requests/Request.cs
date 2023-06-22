using Requests.Options;
using System.Diagnostics;

namespace Requests
{
    /// <summary>
    /// A <see cref="Request{TOptions, TCompleated, TFailed}"/> object that can be managed by the <see cref="RequestHandler"/>.
    /// </summary>
    /// <typeparam name="TOptions">Type of options</typeparam>
    /// <typeparam name="TCompleated">Type of compleated return</typeparam>
    /// <typeparam name="TFailed">Type of failed return</typeparam>
    public abstract class Request<TOptions, TCompleated, TFailed> : RequestObject where TOptions : RequestOptions<TCompleated, TFailed>, new()
    {
        /// <summary>
        /// If this object is disposed of.
        /// </summary>
        private bool _disposed;

        /// <summary>
        /// How often this <see cref="Request{TOptions, TCompleated, TFailed}"/> failded.
        /// </summary>
        public byte AttemptCounter { get; private set; }

        /// <summary>
        /// The <see cref="CancellationTokenSource"/> for this object.
        /// </summary>
        private CancellationTokenSource _cts = null!;

        /// <summary>
        /// The <see cref="CancellationTokenRegistration"/> for this object.
        /// </summary>
        private CancellationTokenRegistration _ctr;

        /// <summary>
        /// The <see cref="RequestState"/> of this <see cref="Request{TOptions, TCompleated, TFailed}"/>.
        /// </summary>
        private RequestState _state = RequestState.Onhold;

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
        public override Task Task => _isFinished.Task;

        /// <summary>
        /// <see cref="AggregateException"/> that contains the throwed Exeptions
        /// </summary>
        public override AggregateException? Exception => _exceptions.Count == 0 ? null : new AggregateException(_exceptions);

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
        public override RequestState State
        {
            get => _state; protected set
            {
                if (_state is RequestState.Compleated or RequestState.Failed or RequestState.Cancelled)
                    return;
                _state = value;
                StateChanged?.Invoke(this);
            }
        }

        /// <summary>
        /// Event that will be invoked when the <see cref="State"/> of this object changed.
        /// </summary>
        public Notify<Request<TOptions, TCompleated, TFailed>>? StateChanged { get; set; }

        /// <summary>
        /// If the <see cref="Request{TOptions, TCompleated, TFailed}"/> has priority over other not prioritized <see cref="Request{TOptions, TCompleated, TFailed}">Requests</see>.
        /// </summary>
        public override RequestPriority Priority => Options.Priority;

        /// <summary>
        /// Consructor of the <see cref="Request{TOptions, TCompleated, TFailed}"/> class 
        /// </summary>
        /// <param name="options">Options to modify the <see cref="Request{TOptions, TCompleated, TFailed}"/></param>
        protected Request(TOptions? options = null)
        {
            _startOptions = options ?? new();
            Options = _startOptions with { };
            NewCTS();
        }

        /// <summary>
        /// Releases all Recouces of <see cref="_ctr"/> and <see cref="_cts"/> and sets them new 
        /// </summary>
        private void NewCTS()
        {
            _cts?.Dispose();
            _ctr.Unregister();
            if (Options.CancellationToken.HasValue)
                _cts = CancellationTokenSource.CreateLinkedTokenSource(Options.Handler.CT, Options.CancellationToken.Value);
            else
                _cts = CancellationTokenSource.CreateLinkedTokenSource(Options.Handler.CT);
            _ctr = Token.Register(() => Options.RequestCancelled?.Invoke());
        }

        /// <summary>
        /// Cancel the <see cref="Request{TOptions, TCompleated, TFailed}"/>
        /// </summary>
        /// /// <exception cref="AggregateException"></exception>
        /// /// <exception cref="ObjectDisposedException"></exception>
        /// /// <exception cref="InvalidOperationException"></exception>
        public override void Cancel()
        {
            if (State == RequestState.Cancelled)
                return;
            State = RequestState.Cancelled;
            if (!_disposed)
                _cts.Cancel();
            _isFinished.TrySetCanceled();
        }

        /// <summary>
        /// Dispose the <see cref="Request{TOptions, TCompleated, TFailed}"/>. 
        /// Will be called automaticly by the <see cref="RequestHandler"/>.
        /// </summary>
        /// <exception cref="AggregateException"></exception>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="ObjectDisposedException"></exception>
        /// <exception cref="InvalidOperationException"></exception>
        public override void Dispose()
        {
            if (State == RequestState.Running)
                Cancel();
            if (_disposed)
                return;
            _disposed = true;

            Options = Options with { RequestCancelled = null, RequestCompleated = null, RequestFailed = null, RequestStarted = null };

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
        internal override async Task StartRequestAsync()
        {
            if (State != RequestState.Available || Options.CancellationToken.HasValue && Options.CancellationToken.Value.IsCancellationRequested)
                return;
            State = RequestState.Running;

            if (_cts.IsCancellationRequested)
            {
                if (Options.Handler.CT.IsCancellationRequested)
                    return;
                NewCTS();
            }

            RequestReturn returnItem = new();

            _ = Options.RequestStarted?.BeginInvoke(null, null);
            try
            {
                returnItem = await RunRequestAsync();
            }
            catch (Exception ex)
            {
                AddException(ex);
                Debug.Assert(false, ex.Message);
            }

            SetResult(returnItem);
        }

        /// <summary>
        /// Sets the result of the Request and handles if it doesn't succeed
        /// </summary>
        /// <param name="returnItem"></param>
        private void SetResult(RequestReturn returnItem)
        {
            if (State == RequestState.Running)
                if (returnItem.Successful)
                {
                    State = RequestState.Compleated;
                    Options.RequestCompleated?.Invoke(returnItem.CompleatedReturn);
                }
                else if (Token.IsCancellationRequested)
                    if (Options.CancellationToken.HasValue && Options.CancellationToken.Value.IsCancellationRequested)
                        State = RequestState.Cancelled;
                    else
                        State = RequestState.Available;
                else if (AttemptCounter++ < Options.NumberOfAttempts)
                    if (Options.DelayBetweenAttemps.HasValue)
                    {
                        State = RequestState.Waiting;
                        Task.Run(() => WaitAndDeploy(Options.DelayBetweenAttemps.Value));
                    }
                    else
                        State = RequestState.Available;
                else
                {
                    State = RequestState.Failed;
                    Options.RequestFailed?.Invoke(returnItem.FailedReturn);
                }

            SetTaskState();
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
                case RequestState.Cancelled:
                    _isFinished.SetCanceled();
                    break;
                case RequestState.Failed:
                    _isFinished.TrySetResult();
                    break;
            }
        }

        /// <summary>
        /// Adds a <see cref="Exception"/> to the <see cref="Exception"/> trace
        /// </summary>
        protected void AddException(Exception exception) => _exceptions.Add(exception);

        /// <summary>
        /// Handles the <see cref="Request{TOptions, TCompleated, TFailed}"/> that the <see cref="HttpClient"/> should start.
        /// </summary>
        /// <returns>A <see cref="RequestReturn"/> object that indicates if the <see cref="Request{TOptions, TCompleated, TFailed}"/> was succesful and returns the return objects.</returns>
        protected abstract Task<RequestReturn> RunRequestAsync();

        /// <summary>
        /// Start the <see cref="Request{TOptions, TCompleated, TFailed}"/> if it is not yet started or paused.
        /// </summary>
        public override void Start()
        {
            if (State != RequestState.Onhold)
                return;
            State = DeployDelay.HasValue ? RequestState.Waiting : RequestState.Available;
            if (DeployDelay.HasValue)
                Task.Run(() => WaitAndDeploy(DeployDelay.Value));
            else
                Options.Handler.RunRequests(this);
        }

        /// <summary>
        /// Waits that the timespan ends to deploy the <see cref="Request{TOptions, TCompleated, TFailed}"/>
        /// </summary>
        /// <param name="timeSpan">Time span to the deploy</param>
        private async Task WaitAndDeploy(TimeSpan timeSpan)
        {
            if (State != RequestState.Waiting)
                return;
            await Task.Delay(timeSpan);
            if (State != RequestState.Waiting)
                return;
            State = RequestState.Available;
            Options.Handler.RunRequests(this);
        }

        /// <summary>
        /// Set the <see cref="Request{TOptions, TCompleated, TFailed}"/> on hold.
        /// </summary>
        public override void Pause() => State = RequestState.Onhold;

        /// <summary>
        /// Class that holds the return and notification objects.
        /// </summary>
        protected class RequestReturn
        {
            /// <summary>
            /// Contructor to set the return types
            /// </summary>
            /// <param name="successful">Bool that indicates success</param>
            /// <param name="compleatedReturn">Object that will be returned if completed</param>
            /// <param name="failedReturn">Object rthat will be retuned if failed</param>
            public RequestReturn(bool successful, TCompleated compleatedReturn, TFailed failedReturn)
            {
                Successful = successful;
                CompleatedReturn = compleatedReturn;
                FailedReturn = failedReturn;
            }

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
            public bool Successful { get; set; } = false;
        }
    }
}
