using Requests.Options;
using System.Collections;
using System.Runtime.CompilerServices;

namespace Requests
{
    /// <summary>
    /// A class that combines multiple <see cref="IRequest"/> instances.
    /// </summary>
    /// <typeparam name="TRequest">A class that implements <see cref="IRequest"/></typeparam>
    public class RequestContainer<TRequest> : IRequestContainer<TRequest> where TRequest : IRequest
    {
        private volatile TRequest[] _requests = [];
        private int _count;
        private bool _isRunning = true;
        private bool _isCanceled = false;
        private bool _disposed = false;
        private TaskCompletionSource? _task;
        private CancellationTokenSource _taskCancellationTokenSource = new();
        private readonly RequestContainerStateMachine _stateMachine;
        private RequestPriority _priority = RequestPriority.Normal;
        private int _writeInProgress; // 0 means no write, 1 means write in progress

        // Cached delegates to avoid allocations
        private static readonly SendOrPostCallback s_stateChangedCallback = static state =>
        {
            (RequestContainer<TRequest> container, RequestState newState) = ((RequestContainer<TRequest>, RequestState))state!;
            container.StateChanged?.Invoke(container, newState);
        };

        /// <summary>
        /// Represents the combined task of the requests.
        /// </summary>
        public Task Task => _task?.Task ?? Task.CompletedTask;

        /// <summary>
        /// Represents the current state of this <see cref="RequestContainer{TRequest}"/>.
        /// </summary>
        public RequestState State { get => _stateMachine.Current; private set => _stateMachine.TryTransition(value); }

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
        public int Count => _count;

        /// <summary>
        /// The synchronization context captured when this object was created. This will never be null.
        /// </summary>
        protected SynchronizationContext SynchronizationContext { get; }

        /// <summary>
        /// All exceptions that were thrown by the requests.
        /// </summary>
        public AggregateException? Exception => new(GetStored().Where(x => x?.Exception != null).Select(x => x!.Exception!));

        /// <summary>
        /// Represents a task that completes when all the requests currently available in the container have completed.
        /// This task does not include requests that may be added to the container in the future.
        /// </summary>
        public Task CurrentTask => Task.WhenAll(GetStored().Select(request => request.Task));

        /// <inheritdoc/>
        IRequest? IRequest.SubsequentRequest => null;

        /// <summary>
        /// Constructor that merges <see cref="IRequest"/> instances together.
        /// </summary>
        /// <param name="requests">The <see cref="IRequest"/> instances to merge.</param>
        public RequestContainer(params TRequest[] requests) : this() => AddRange(requests);

        /// <summary>
        /// Default constructor for <see cref="RequestContainer{TRequest}"/>.
        /// </summary>
        public RequestContainer()
        {
            SynchronizationContext = SynchronizationContext.Current ?? new();
            _stateMachine = new RequestContainerStateMachine(RequestState.Paused, OnStateChanged);
        }

        /// <summary>
        /// Callback when state changes.
        /// </summary>
        private void OnStateChanged(RequestState oldState, RequestState newState) =>
            SynchronizationContext.Post(s_stateChangedCallback, (this, newState));

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
                    _requests[key].StateChanged -= OnRequestStateChanged;
                    _requests[key] = value;

                    if (_isCanceled)
                        _requests[key].Cancel();
                    else if (_disposed)
                        _requests[key].Dispose();
                    else if (!_isRunning)
                        _requests[key].Pause();

                    _requests[key].StateChanged += OnRequestStateChanged;
                    NewTaskCompletion();
                    OnRequestStateChanged(this, _requests[key].State);
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
            foreach (RequestContainer<TRequest> container in requestContainers)
                newContainer.AddRange(container.ToArray());
            return newContainer;
        }

        /// <summary>
        /// Incorporates a <see cref="IRequest"/> into the <see cref="RequestContainer{TRequest}"/>.
        /// </summary>
        /// <param name="request">The <see cref="IRequest"/> to be incorporated.</param>
        public virtual void Add(TRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);

            if (_isCanceled)
                request.Cancel();
            else if (_disposed)
                request.Dispose();
            else if (!_isRunning)
                request.Pause();

            request.StateChanged += OnRequestStateChanged;

            // Acquire write lock with spin
            while (Interlocked.CompareExchange(ref _writeInProgress, 1, 0) == 1)
                Thread.Yield();

            try
            {
                if (_requests.Length == _count)
                    Grow();

                _requests[_count] = request;
                _count++;
            }
            finally
            {
                Interlocked.Exchange(ref _writeInProgress, 0); // Release the write lock
            }

            NewTaskCompletion();
            OnRequestStateChanged(this, request.State);
        }

        /// <summary>
        /// Incorporates multiple <see cref="IRequest"/> into the <see cref="RequestContainer{TRequest}"/>.
        /// </summary>
        /// <param name="requests">The <see cref="IRequest"/> to be incorporated.</param>
        public virtual void AddRange(params TRequest[] requests)
        {
            ArgumentNullException.ThrowIfNull(requests);

            if (_isCanceled)
            {
                foreach (TRequest request in requests)
                    request.Cancel();
            }
            else if (_disposed)
            {
                foreach (TRequest request in requests)
                    request.Dispose();
            }
            else if (!_isRunning)
            {
                foreach (TRequest request in requests)
                    request.Pause();
            }

            foreach (TRequest request in requests)
                request.StateChanged += OnRequestStateChanged;

            // Acquire write lock with spin
            while (Interlocked.CompareExchange(ref _writeInProgress, 1, 0) == 1)
                Thread.Yield();

            try
            {
                while (_requests.Length < _count + requests.Length)
                    Grow();

                Array.Copy(requests, 0, _requests, _count, requests.Length);
                _count += requests.Length;
            }
            finally
            {
                Interlocked.Exchange(ref _writeInProgress, 0); // Release the write lock
            }

            NewTaskCompletion();
            State = CalculateState();
        }

        /// <summary>
        /// Removes a specific <see cref="IRequest"/> from this container.
        /// </summary>
        /// <param name="requests">The request to be removed.</param>
        public virtual void Remove(params TRequest[] requests)
        {
            if (requests == null || requests.Length == 0)
                throw new ArgumentNullException(nameof(requests));

            foreach (TRequest request in requests)
            {
                if (request == null)
                    throw new ArgumentNullException(nameof(requests), "Individual request cannot be null.");

                request.StateChanged -= OnRequestStateChanged;
            }

            // Acquire write lock with spin
            while (Interlocked.CompareExchange(ref _writeInProgress, 1, 0) == 1)
                Thread.Yield();

            try
            {
                TRequest[] storedRequests = GetStored().Where(x => !requests.Any(y => y.Equals(x))).ToArray();

                int size = storedRequests.Length;
                int newSize = size + 32;

                Array.Resize(ref storedRequests, newSize);

                _requests = storedRequests;
                _count = size;
            }
            finally
            {
                Interlocked.Exchange(ref _writeInProgress, 0);
            }

            if (_count > 0 && !Task.IsCompleted)
                NewTaskCompletion();
            else
                _task = null;

            if (State is not RequestState.Completed and not RequestState.Paused)
                State = CalculateState();
        }

        /// <summary>
        /// Increases the capacity of the <see cref="RequestContainer{TRequest}"/> to accommodate additional elements.
        /// </summary>
        private void Grow()
        {
            const int MinimumGrow = 4;
            int capacity = (int)(_requests.Length * 2L);
            if (capacity < _requests.Length + MinimumGrow)
                capacity = _requests.Length + MinimumGrow;

            TRequest[] newArray = new TRequest[capacity];
            _requests.CopyTo(newArray, 0);
            _requests = newArray;
        }

        /// <summary>
        /// Initializes a new TaskCompletionSource and cancels the previous one.
        /// </summary>
        private void NewTaskCompletion()
        {
            _taskCancellationTokenSource.Cancel();
            _taskCancellationTokenSource = new();

            if (Task.IsCompleted)
                _task = new(TaskCreationOptions.RunContinuationsAsynchronously);

            _ = Task.WhenAll(GetStored().Select(request => request.Task))
                .ContinueWith(task => _task?.TrySetResult(), _taskCancellationTokenSource.Token);
        }

        /// <summary>
        /// Handles the state change event of individual requests in the container.
        /// </summary>
        /// <param name="sender">The sender of the event.</param>
        /// <param name="state">The new state.</param>
        private void OnRequestStateChanged(object? sender, RequestState state)
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
            IEnumerable<int> states = GetStored().Select(req => (int)req.State);
            int[] counter = new int[7];

            foreach (int value in states)
                counter[value]++;

            // Priority order for state calculation
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
            else if (counter[2] == _count)
                state = RequestState.Completed;
            else if (counter[3] > 0)
                state = RequestState.Paused;
            else
                state = (RequestState)Array.IndexOf(counter, counter.Max());

            return state;
        }

        /// <summary>
        /// Gets the stored requests that are non-null.
        /// </summary>
        /// <returns>An enumerable of non-null requests.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private IEnumerable<TRequest> GetStored() => _requests[.._count].Where(req => req != null)!;

        /// <summary>
        /// Sets the priority for the <see cref="RequestContainer{TRequest}"/>.
        /// Not to the contained <see cref="IRequest"/> objects.
        /// </summary>
        public void SetPriority(RequestPriority priority) => _priority = priority;

        /// <summary>
        /// Initiates all <see cref="IRequest"/> if they are currently on hold.
        /// </summary>
        public void Start()
        {
            if (_isRunning)
                return;

            _isRunning = true;

            foreach (TRequest request in GetStored())
                request.Start();
        }

        /// <summary>
        /// Pauses all <see cref="IRequest"/> in the container.
        /// </summary>
        public void Pause()
        {
            _isRunning = false;

            foreach (TRequest request in GetStored())
                request.Pause();
        }

        /// <summary>
        /// Cancels all <see cref="Request{TOptions, TCompleted, TFailed}"/> in the container.
        /// </summary>
        public void Cancel()
        {
            _isCanceled = true;

            foreach (TRequest request in GetStored())
                request.Cancel();
        }

        /// <summary>
        /// Attempts to set all <see cref="IRequest"/> objects in the container's <see cref="State"/> to idle.
        /// This is possible only if none of them are completed, failed, or cancelled.
        /// </summary>
        /// <returns>True if all <see cref="IRequest"/> objects are in an idle <see cref="RequestState"/>, otherwise false.</returns>
        public bool TrySetIdle()
        {
            foreach (TRequest request in GetStored())
                _ = request.TrySetIdle();

            return State == RequestState.Idle;
        }

        /// <summary>
        /// Since the container's state depends on the items it holds, it cannot complete permanently.
        /// Container can always accept new requests.
        /// </summary>
        /// <returns>Always returns <c>false</c></returns>
        public bool HasCompleted() => false;

        /// <summary>
        /// Yield point for IRequest interface compatibility.
        /// Containers don't yield in the same way as individual requests - this is a no-op.
        /// </summary>
        /// <returns>A completed ValueTask.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ValueTask YieldAsync() => ValueTask.CompletedTask;

        /// <summary>
        /// Waits for all currently stored requests to complete.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Task that completes when all current requests are done.</returns>
        public async Task WaitForCurrentRequestsAsync(CancellationToken cancellationToken = default)
        {
            TRequest[] snapshot = [.. GetStored()];
            if (snapshot.Length == 0)
                return;

            await Task.WhenAll(snapshot.Select(r => r.Task)).ConfigureAwait(false);
        }

        /// <summary>
        /// Executes all requests in the container.
        /// </summary>
        async Task IRequest.StartRequestAsync()
        {
            if (_isRunning)
                return;

            _isRunning = true;

            foreach (TRequest request in GetStored())
            {
                if (request.State != RequestState.Running)
                    _ = request.TrySetIdle();
            }

            foreach (TRequest request in GetStored().Where(x => x.State == RequestState.Idle))
                await request.StartRequestAsync().ConfigureAwait(false);

            _isRunning = false;
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

            foreach (TRequest request in GetStored())
                request.Dispose();

            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Provides an enumerator that iterates through the <see cref="RequestContainer{TRequest}"/> 
        /// </summary>
        /// <returns> A  <see cref="RequestContainer{TRequest}"/> .Enumerator for the <see cref="RequestContainer{TRequest}"/> .</returns>
        public IEnumerator<TRequest> GetEnumerator() => GetStored().GetEnumerator();

        /// <inheritdoc/>
        IEnumerator IEnumerable.GetEnumerator() => GetStored().GetEnumerator();
    }
}