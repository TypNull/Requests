using Requests.Options;

namespace Requests
{
    /// <summary>
    /// Class to manage and merge more than one TRequest.
    /// </summary>
    /// <typeparam name="TRequest">A RequestObject class</typeparam>
    public class RequestContainer<TRequest> : RequestObject where TRequest : RequestObject
    {
        private readonly List<TRequest> _requests = new();
        private bool _isrunning = true;
        private bool _isCanceled = false;
        private bool _disposed = false;
        private Task _task = Task.CompletedTask;

        /// <summary>
        /// Merged task out the requests
        /// </summary>
        public override Task Task => _task;

        /// <summary>
        /// State of this <see cref="RequestContainer{TRequest}"/>
        /// </summary>
        public override RequestState State { get => RequestState.Running; protected set { } }

        /// <summary>
        /// Priority is unnessesary but always Normal
        /// </summary>
        public override RequestPriority Priority => RequestPriority.Normal;

        /// <summary>
        /// All exceptions that were risen by the requests
        /// </summary>
        public override AggregateException? Exception => new(_requests.Where(x => x.Exception != null).Select(x => x.Exception!));

        /// <summary>
        /// Constructor to merge <see cref="RequestObject"/> together
        /// </summary>
        /// <param name="requests"><see cref="RequestObject"/>s to merge</param>
        public RequestContainer(params TRequest[] requests) => Add(requests);

        /// <summary>
        /// Get all <see cref="RequestObject"/> in this Container
        /// </summary>
        /// <returns>returns a <see cref="RequestObject"/> array</returns>
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
        /// Adds a <see cref="RequestObject"/> to the <see cref="RequestContainer{TRequest}"/>.
        /// </summary>
        /// <param name="request">The <see cref="RequestObject"/> to add.</param>
        public virtual void Add(TRequest request)
        {
            if (_isCanceled)
                request.Cancel();
            else if (_disposed)
                request.Dispose();
            else if (!_isrunning)
                request.Pause();

            _requests.Add(request);
            _task = Task.WhenAll(_requests.Select(request => request.Task));
        }

        internal override async Task StartRequestAsync()
        {
            _isrunning = true;
            foreach (TRequest? request in _requests)
                await request.StartRequestAsync();
        }

        /// <summary>
        /// Adds a range <see cref="RequestObject"/> to the <see cref="RequestContainer{TRequest}"/>.
        /// </summary>
        /// <param name="requests">The <see cref="RequestObject"/> to add.</param>
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
        /// Removes a <see cref="RequestObject"/> from this container.
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
        public override void Cancel()
        {
            _isCanceled = true;
            _requests.ForEach(request => request.Cancel());
        }

        /// <summary>
        /// Starts all <see cref="RequestObject"/> if they are on hold
        /// </summary>
        public override void Start()
        {
            _isrunning = true;
            foreach (TRequest? request in _requests)
                request.Start();
        }
        /// <summary>
        /// Put every <see cref="RequestObject"/> in Container on hold
        /// </summary>
        public override void Pause()
        {
            _isrunning = false;
            foreach (TRequest? request in _requests)
                request.Pause();
        }

        /// <summary>
        /// Dispose all requests in container
        /// </summary>
        public override void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            foreach (TRequest? request in _requests)
                request.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
