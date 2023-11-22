using Requests.Options;
using System.Collections;

namespace Requests
{
    /// <summary>
    /// Class to manage and merge more than one TRequest.
    /// </summary>
    /// <typeparam name="TRequest">A RequestObject class</typeparam>
    public class RequestContainer<TRequest> : IEnumerable<TRequest>, IRequest where TRequest : IRequest
    {
        private readonly List<TRequest> _requests = new();
        private bool _isrunning = true;
        private bool _isCanceled = false;
        private bool _disposed = false;
        private TaskCompletionSource? _task;
        private CancellationTokenSource _taskCancelationTokenSource = new();
        private RequestState _state = RequestState.Paused;

        /// <summary>
        /// Merged task out the requests
        /// </summary>
        public Task Task => _task?.Task ?? Task.CompletedTask;

        /// <summary>
        /// State of this <see cref="RequestContainer{TRequest}"/>
        /// </summary>
        public RequestState State
        {
            get { return _state; }
            protected set
            {
                if (_state == value)
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
        /// Priority is unnessesary but always Normal
        /// </summary>
        public RequestPriority Priority => RequestPriority.Normal;

        /// <summary>
        /// Gets the number of <see cref="IRequest"/> conntained in the <see cref="RequestContainer{TRequest}"/>
        /// </summary>
        public int Count => _requests.Count;

        /// <summary>
        /// The synchronization context captured upon construction. This will never be null.
        /// </summary>
        protected SynchronizationContext SynchronizationContext { get; }

        /// <summary>
        /// All exceptions that were risen by the requests
        /// </summary>
        public AggregateException? Exception => new(_requests.Where(x => x.Exception != null).Select(x => x.Exception!));

        /// <summary>
        /// Constructor to merge <see cref="IRequest"/> together
        /// </summary>
        /// <param name="requests"><see cref="IRequest"/>s to merge</param>
        public RequestContainer(params TRequest[] requests) : this() => AddRange(requests);

        /// <summary>
        /// Get all <see cref="IRequest"/> in this Container
        /// </summary>
        /// <returns>returns a <see cref="IRequest"/> array</returns>
        public TRequest this[int key]
        {
            get => _requests[key];
            set => _requests[key] = value;
        }

        /// <summary>
        /// Creates a new <see cref="RequestContainer{TRequest}"/> that megres  <see cref="RequestContainer{TRequest}"/> together.
        /// </summary>
        /// <param name="requestContainers">Other <see cref="RequestContainer{TRequest}"/> to merge</param>
        /// <returns></returns>
        public static RequestContainer<TRequest> MergeContainers(params RequestContainer<TRequest>[] requestContainers)
        {
            RequestContainer<TRequest> newContainer = new();
            Array.ForEach(requestContainers, requestContainer => newContainer.AddRange(requestContainer.ToArray()));
            return newContainer;
        }

        /// <summary>
        /// Main Contructor for <see cref="RequestContainer{TRequest}"/>.
        /// </summary>
        public RequestContainer() => SynchronizationContext = SynchronizationContext.Current ?? new();

        /// <summary>
        /// Adds a <see cref="IRequest"/> to the <see cref="RequestContainer{TRequest}"/>.
        /// </summary>
        /// <param name="request">The <see cref="IRequest"/> to add.</param>
        public virtual void Add(TRequest request)
        {
            if (_isCanceled)
                request.Cancel();
            else if (_disposed)
                request.Dispose();
            else if (!_isrunning)
                request.Pause();

            request.StateChanged += OnStateChanged;
            _requests.Add(request);
            NewTaskCompletion();
            OnStateChanged(this, request.State);
        }

        private void NewTaskCompletion()
        {
            _taskCancelationTokenSource.Cancel();
            _taskCancelationTokenSource = new();
            if (Task.IsCompleted)
                _task = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Task.WhenAll(_requests.Select(request => request.Task)).ContinueWith(task => _task?.TrySetResult(), _taskCancelationTokenSource.Token);
        }

        /// <summary>
        /// Adds a range <see cref="IRequest"/> to the <see cref="RequestContainer{TRequest}"/>.
        /// </summary>
        /// <param name="requests">The <see cref="IRequest"/> to add.</param>
        public virtual void AddRange(params TRequest[] requests)
        {
            if (_isCanceled)
                Array.ForEach(requests, request => request.Cancel());
            else if (_disposed)
                Array.ForEach(requests, request => request.Dispose());
            else if (!_isrunning)
                Array.ForEach(requests, request => request.Pause());
            Array.ForEach(requests, request => request.StateChanged += OnStateChanged);
            _requests.AddRange(requests);
            NewTaskCompletion();
            State = CalculateState();
        }


        private void OnStateChanged(object? sender, RequestState state)
        {
            if (state == State)
                return;
            if (state != RequestState.Failed)
                state = CalculateState();
            State = state;
        }

        private RequestState CalculateState()
        {
            RequestState state;
            IEnumerable<int> states = _requests.Select(req => (int)req.State);
            int[] counter = new int[7];
            foreach (int value in states)
                counter[value]++;

            if (counter[6] > 0)
                state = RequestState.Failed;
            else if (counter[1] > 0)
                state = RequestState.Running;
            else if (counter[5] > 0)
                state = RequestState.Cancelled;
            else if (counter[0] > 0)
                state = RequestState.Idle;
            else if (counter[4] > 0)
                state = RequestState.Waiting;
            else if (counter[2] == _requests.Count)
                state = RequestState.Compleated;
            else if (counter[3] > 0)
                state = RequestState.Paused;
            else
                state = (RequestState)Array.IndexOf(counter, counter.Max());

            return state;
        }

        async Task IRequest.StartRequestAsync()
        {
            _isrunning = true;
            foreach (TRequest? request in _requests)
                await request.StartRequestAsync();
        }

        /// <summary>
        /// Removes a <see cref="IRequest"/> from this container.
        /// </summary>
        /// <param name="requests">Request to remove</param>
        public virtual void Remove(params TRequest[] requests)
        {
            Array.ForEach(requests, request =>
            {
                _requests.Remove(request);
                request.StateChanged -= StateChanged;
            });
            if (_requests.Count > 0 && !Task.IsCompleted)
                NewTaskCompletion();
            else
                _task = null;
            if (State is not RequestState.Compleated and not RequestState.Paused)
                State = CalculateState();

        }

        /// <summary>
        /// Cancel all <see cref="Request{TOptions, TCompleated, TFailed}"/> in container
        /// </summary>
        public void Cancel()
        {
            _isCanceled = true;
            _requests.ForEach(request => request.Cancel());
        }

        /// <summary>
        /// Starts all <see cref="IRequest"/> if they are on hold
        /// </summary>
        public void Start()
        {
            _isrunning = true;
            foreach (TRequest? request in _requests)
                request.Start();
        }

        /// <summary>
        /// Put every <see cref="IRequest"/> in Container on hold
        /// </summary>
        public void Pause()
        {
            _isrunning = false;
            foreach (TRequest? request in _requests)
                request.Pause();
        }

        /// <summary>
        /// Dispose all requests in container
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            StateChanged = null;
            foreach (TRequest? request in _requests)
                request.Dispose();
            GC.SuppressFinalize(this);
        }

        /// <summary>
        ///    Returns an enumerator that iterates through the <see cref="RequestContainer{TRequest}"/> 
        /// </summary>
        /// <returns> A  <see cref="RequestContainer{TRequest}"/> .Enumerator for the <see cref="RequestContainer{TRequest}"/> .</returns>
        public IEnumerator<TRequest> GetEnumerator() => _requests.GetEnumerator();

        /// <inheritdoc/>
        IEnumerator IEnumerable.GetEnumerator() => _requests.GetEnumerator();
    }
}
