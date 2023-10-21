using Requests.Options;

namespace Requests
{
    /// <summary>
    /// Class to manage and merge more than one TRequest.
    /// </summary>
    /// <typeparam name="TRequest">A RequestObject class</typeparam>
    public class RequestContainer<TRequest> : IRequest where TRequest : IRequest
    {
        private readonly List<TRequest> _requests = new();
        private bool _isrunning = true;
        private bool _isCanceled = false;
        private bool _disposed = false;
        private Task _task = Task.CompletedTask;
        private RequestState _state = RequestState.Paused;

        /// <summary>
        /// Merged task out the requests
        /// </summary>
        public Task Task => _task;

        /// <summary>
        /// State of this <see cref="RequestContainer{TRequest}"/>
        /// </summary>
        public RequestState State
        {
            get { return _state; }
            protected set
            {
                _state = value;
                StateChanged?.Invoke(this, value);
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
        /// All exceptions that were risen by the requests
        /// </summary>
        public AggregateException? Exception => new(_requests.Where(x => x.Exception != null).Select(x => x.Exception!));

        /// <summary>
        /// Constructor to merge <see cref="IRequest"/> together
        /// </summary>
        /// <param name="requests"><see cref="IRequest"/>s to merge</param>
        public RequestContainer(params TRequest[] requests) => Add(requests);

        /// <summary>
        /// Get all <see cref="IRequest"/> in this Container
        /// </summary>
        /// <returns>returns a <see cref="IRequest"/> array</returns>
        public IReadOnlyList<TRequest> GetRequests() => _requests;

        /// <summary>
        /// Creates a new <see cref="RequestContainer{TRequest}"/> that megres  <see cref="RequestContainer{TRequest}"/> together.
        /// </summary>
        /// <param name="requestContainers">Other <see cref="RequestContainer{TRequest}"/> to merge</param>
        /// <returns></returns>
        public static RequestContainer<TRequest> MergeContainers(params RequestContainer<TRequest>[] requestContainers)
        {
            List<TRequest> requests = new();
            Array.ForEach(requestContainers, requestContainer => requests.AddRange(requestContainer._requests));
            return new RequestContainer<TRequest>(requests.ToArray());
        }

        /// <summary>
        /// Main Contructor for <see cref="RequestContainer{TRequest}"/>.
        /// </summary>
        public RequestContainer()
        { }

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
            _task = Task.WhenAll(_requests.Select(request => request.Task));
        }

        private void OnStateChanged(object? sender, RequestState state)
        {
            if (state == State)
                return;
            if (state != RequestState.Failed)
            {
                int[] states = _requests.Select(req => (int)req.State).ToArray();
                int[] counter = new int[7];
                foreach (int value in states)
                    counter[value]++;

                if (counter[(int)RequestState.Running] > 1)
                    state = RequestState.Running;
                else if (counter[(int)RequestState.Idle] > 1)
                    state = RequestState.Idle;
                else
                {
                    int max = counter.Max();
                    state = (RequestState)counter.ToList().IndexOf(max);
                }
            }
            if (state != State)
                State = state;
        }

        async Task IRequest.StartRequestAsync()
        {
            _isrunning = true;
            foreach (TRequest? request in _requests)
                await request.StartRequestAsync();
        }

        /// <summary>
        /// Adds a range <see cref="IRequest"/> to the <see cref="RequestContainer{TRequest}"/>.
        /// </summary>
        /// <param name="requests">The <see cref="IRequest"/> to add.</param>
        public virtual void Add(params TRequest[] requests)
        {
            if (_isCanceled)
                Array.ForEach(requests, request => request.Cancel());
            else if (_disposed)
                Array.ForEach(requests, request => request.Dispose());
            else if (!_isrunning)
                Array.ForEach(requests, request => request.Pause());

            _requests.AddRange(requests);
            _task = Task.WhenAll(_requests.Select(request => request.Task));
        }

        /// <summary>
        /// Removes a <see cref="IRequest"/> from this container.
        /// </summary>
        /// <param name="requests">Request to remove</param>
        public virtual void Remove(params TRequest[] requests)
        {
            Array.ForEach(requests, request => _requests.Remove(request));
            _task = Task.WhenAll(_requests.Select(request => request.Task));
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
    }
}
