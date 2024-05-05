using Requests.Options;
using System.Collections;

namespace Requests
{
    /// <summary>
    /// A class that combines multiple <see cref="IRequest"/> instances.
    /// </summary>
    /// <typeparam name="TRequest">A class that implements <see cref="IRequest"/></typeparam>
    public class RequestContainer<TRequest> : IEnumerable<TRequest>, IRequest where TRequest : IRequest
    {
        private volatile TRequest[] _requests = Array.Empty<TRequest>();
        private bool _isrunning = true;
        private bool _isCanceled = false;
        private bool _disposed = false;
        private TaskCompletionSource? _task;
        private CancellationTokenSource _taskCancelationTokenSource = new();
        private RequestState _state = RequestState.Paused;
        private RequestPriority _priority = RequestPriority.Normal;

        /// <summary>
        /// Represents the combined task of the requests.
        /// </summary>
        public Task Task => _task?.Task ?? Task.CompletedTask;

        /// <summary>
        /// Represents the current state of this <see cref="RequestContainer{TRequest}"/>.
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
        /// Event triggered when the <see cref="State"/> of this object changes.
        /// </summary>
        public event EventHandler<RequestState>? StateChanged;

        /// <summary>
        /// The priority of this request container.
        /// </summary>
        public RequestPriority Priority => _priority;

        /// <summary>
        /// Gets the count of <see cref="IRequest"/> instances contained in the <see cref="RequestContainer{TRequest}"/>.
        /// </summary>
        public int Length => _requests.Length;

        /// <summary>
        /// The synchronization context captured when this object was created. This will never be null.
        /// </summary>
        protected SynchronizationContext SynchronizationContext { get; }

        /// <summary>
        /// All exceptions that were thrown by the requests.
        /// </summary>
        public AggregateException? Exception => new(_requests.Where(x => x.Exception != null).Select(x => x.Exception!));

        /// <summary>
        /// Constructor that merges <see cref="IRequest"/> instances together.
        /// </summary>
        /// <param name="requests">The <see cref="IRequest"/> instances to merge.</param>
        public RequestContainer(params TRequest[] requests) : this() => AddRange(requests);

        /// <summary>
        /// Accesses all <see cref="IRequest"/> instances in this container.
        /// </summary>
        /// <returns>Returns an array of <see cref="IRequest"/> instances.</returns>
        public virtual TRequest this[int key]
        {
            get => _requests[key];
            set
            {
                if (!_requests[key].Equals(value))
                {
                    _requests[key].StateChanged -= StateChanged;
                    _requests[key] = value;
                    if (_isCanceled)
                        _requests[key].Cancel();
                    else if (_disposed)
                        _requests[key].Dispose();
                    else if (!_isrunning)
                        _requests[key].Pause();

                    _requests[key].StateChanged += OnStateChanged;
                    NewTaskCompletion();
                    OnStateChanged(this, _requests[key].State);
                }
            }
        }

        /// <summary>
        /// Creates a new <see cref="RequestContainer{TRequest}"/> that merges multiple <see cref="RequestContainer{TRequest}"/> instances together.
        /// </summary>
        /// <param name="requestContainers">The <see cref="RequestContainer{TRequest}"/> instances to merge.</param>
        /// <returns>Returns a new <see cref="RequestContainer{TRequest}"/> instance.</returns>
        public static RequestContainer<TRequest> MergeContainers(params RequestContainer<TRequest>[] requestContainers)
        {
            RequestContainer<TRequest> newContainer = new();
            Array.ForEach(requestContainers, requestContainer => newContainer.AddRange(requestContainer.ToArray()));
            return newContainer;
        }

        /// <summary>
        /// Default constructor for <see cref="RequestContainer{TRequest}"/>.
        /// </summary>
        public RequestContainer() => SynchronizationContext = SynchronizationContext.Current ?? new();

        /// <summary>
        /// Incorporates a <see cref="IRequest"/> into the <see cref="RequestContainer{TRequest}"/>.
        /// </summary>
        /// <param name="request">The <see cref="IRequest"/> to be incorporated.</param>
        public virtual void Add(TRequest request)
        {
            if (_isCanceled)
                request.Cancel();
            else if (_disposed)
                request.Dispose();
            else if (!_isrunning)
                request.Pause();

            request.StateChanged += OnStateChanged;
            _requests = CreateArrayWithNewItems(request);
            NewTaskCompletion();
            OnStateChanged(this, request.State);
        }

        /// <summary>
        /// Initializes a new TaskCompletionSource and cancels the previous one.
        /// </summary>
        private void NewTaskCompletion()
        {
            _taskCancelationTokenSource.Cancel();
            _taskCancelationTokenSource = new();
            if (Task.IsCompleted)
                _task = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Task.WhenAll(_requests.Select(request => request.Task)).ContinueWith(task => _task?.TrySetResult(), _taskCancelationTokenSource.Token);
        }

        /// <summary>
        /// Sets the priority for the <see cref="RequestContainer{TRequest}"/>.
        /// Not to the contained <see cref="IRequest"/> objects.
        /// </summary>
        public void SetPriority(RequestPriority priority) => _priority = priority;

        /// <summary>
        /// Incorporates multiple <see cref="IRequest"/> into the <see cref="RequestContainer{TRequest}"/>.
        /// </summary>
        /// <param name="requests">The <see cref="IRequest"/> to be incorporated.</param>
        public virtual void AddRange(params TRequest[] requests)
        {
            if (_isCanceled)
                Array.ForEach(requests, request => request.Cancel());
            else if (_disposed)
                Array.ForEach(requests, request => request.Dispose());
            else if (!_isrunning)
                Array.ForEach(requests, request => request.Pause());
            Array.ForEach(requests, request => request.StateChanged += OnStateChanged);
            _requests = CreateArrayWithNewItems(requests);
            NewTaskCompletion();
            State = CalculateState();
        }

        /// <summary>
        /// Creates a new array that includes the existing requests and the new items.
        /// </summary>
        /// <param name="items">The new items to be added to the array.</param>
        /// <returns>A new array containing the existing and new items.</returns>
        private TRequest[] CreateArrayWithNewItems(params TRequest[] items)
        {
            TRequest[] result = new TRequest[_requests.Length + items.Length];
            _requests.CopyTo(result, 0);
            items.CopyTo(result, _requests.Length);
            return result;
        }

        /// <summary>
        /// Handles the state change event of the <see cref="RequestContainer{TRequest}"/>.
        /// </summary>
        /// <param name="sender">The sender of the event.</param>
        /// <param name="state">The new state.</param>
        private void OnStateChanged(object? sender, RequestState state)
        {
            if (state == State)
                return;
            if (state != RequestState.Failed)
                state = CalculateState();
            State = state;
        }

        /// <summary>
        /// Calculates the current state of the <see cref="RequestContainer{TRequest}"/> based on the states of its requests.
        /// </summary>
        /// <returns>The calculated state of the <see cref="RequestContainer{TRequest}"/>.</returns>
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
            else if (counter[2] == Length)
                state = RequestState.Compleated;
            else if (counter[3] > 0)
                state = RequestState.Paused;
            else
                state = (RequestState)Array.IndexOf(counter, counter.Max());

            return state;
        }

        async Task IRequest.StartRequestAsync()
        {
            if (_isrunning)
                return;
            _isrunning = true;
            foreach (TRequest request in _requests)
                _ = TrySetIdle();
            foreach (TRequest request in _requests.Where(x => x.State == RequestState.Idle))
                await request.StartRequestAsync();


            _isrunning = false;
        }

        /// <summary>
        /// Removes a specific <see cref="IRequest"/> from this container.
        /// </summary>
        /// <param name="requests">The request to be removed.</param>
        public virtual void Remove(params TRequest[] requests)
        {
            Array.ForEach(requests, request => request.StateChanged -= StateChanged);
            _requests = _requests.Where(x => !requests.Any(y => y.Equals(x))).ToArray();
            if (_requests.Length > 0 && !Task.IsCompleted)
                NewTaskCompletion();
            else
                _task = null;
            if (State is not RequestState.Compleated and not RequestState.Paused)
                State = CalculateState();
        }

        /// <summary>
        /// Cancels all <see cref="Request{TOptions, TCompleated, TFailed}"/> in the container.
        /// </summary>
        public void Cancel()
        {
            _isCanceled = true;
            Array.ForEach(_requests, request => request.Cancel());
        }

        /// <summary>
        /// Initiates all <see cref="IRequest"/> if they are currently on hold.
        /// </summary>
        public void Start()
        {
            if (_isrunning)
                return;
            _isrunning = true;
            foreach (TRequest? request in _requests)
                request.Start();
        }

        /// <summary>
        /// Pauses all <see cref="IRequest"/> in the container.
        /// </summary>
        public void Pause()
        {
            _isrunning = false;
            foreach (TRequest? request in _requests)
                request.Pause();
        }

        /// <summary>
        /// Disposes all requests in the container.
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
        /// Provides an enumerator that iterates through the <see cref="RequestContainer{TRequest}"/> 
        /// </summary>
        /// <returns> A  <see cref="RequestContainer{TRequest}"/> .Enumerator for the <see cref="RequestContainer{TRequest}"/> .</returns>
        public IEnumerator<TRequest> GetEnumerator() => ((IEnumerable<TRequest>)_requests).GetEnumerator();

        /// <inheritdoc/>
        IEnumerator IEnumerable.GetEnumerator() => _requests.GetEnumerator();

        /// <summary>
        /// Attempts to set all <see cref="IRequest"/> objects in the container's <see cref="State"/> to idle.
        /// This is possible only if none of them are completed, failed, or cancelled.
        /// </summary>
        /// <returns>True if all <see cref="IRequest"/> objects are in an idle <see cref="RequestState"/>, otherwise false.</returns>
        public bool TrySetIdle()
        {
            foreach (TRequest request in _requests)
                _ = request.TrySetIdle();
            return State == RequestState.Idle;
        }
    }
}
