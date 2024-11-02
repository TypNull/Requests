﻿using Requests.Channel;
using Requests.Options;

namespace Requests
{
    /// <summary>
    /// The <see cref="RequestHandler"/> class is responsible for executing instances of the <see cref="IRequest"/> interface.
    /// </summary>
    public class RequestHandler : IRequestContainer<IRequest>
    {
        /// <summary>
        /// A priority channel that queues all incoming instances of the <see cref="IRequest"/> interface.
        /// </summary>
        private readonly IPriorityChannel<IRequest> _requestsChannel;
        private bool _disposed;

        /// <summary>
        /// Represents the current state of this <see cref="RequestHandler"/>.
        /// </summary>
        public RequestState State
        {
            get { return _state; }
            protected set
            {
                if (_state == value)
                    return;
                _state = value;
                DefaultSynchronizationContext.Post((o) => StateChanged?.Invoke((IRequest)o!, value), this);
            }
        }
        private RequestState _state = RequestState.Idle;

        /// <summary>
        /// Event triggered when the <see cref="State"/> of this object changes.
        /// </summary>
        public event EventHandler<RequestState>? StateChanged;

        /// <summary>
        /// The priority of this request handler.
        /// </summary>
        public RequestPriority Priority => _priority;
        private RequestPriority _priority;


        /// <summary>
        /// Represents the combined task of the requests.
        /// </summary>
        public Task Task => _task.Task;
        private readonly TaskCompletionSource _task = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        /// Gets the aggregate exception associated with the <see cref="RequestHandler"/> instance.
        /// Currently, this property always returns <c>null</c>, indicating that no exceptions are associated with the handler.
        /// </summary>
        public AggregateException? Exception => null;

        /// <summary>
        /// Property that sets the degree of parallel execution of instances of the <see cref="IRequest"/> interface. 
        /// Disables AutoParallelism when set. Default value is null.
        /// </summary>
        public int? StaticDegreeOfParallelism
        {
            get => _staticDegreeOfParallelism; set
            {
                _requestsChannel.Options.MaxDegreeOfParallelism = value ?? AutoParallelism.Invoke();
                _staticDegreeOfParallelism = value;
            }
        }
        private int? _staticDegreeOfParallelism;

        /// <summary>
        /// A function that calculates the degree of parallel execution of instances of the <see cref="IRequest"/> interface dynamically while running.
        /// </summary>
        public Func<int> AutoParallelism { get; set; } = () => Environment.ProcessorCount;

        /// <summary>
        /// Property that sets the maximum possible degree of parallel execution of instances of the <see cref="IRequest"/> interface.
        /// </summary>
        public int MaxParallelism { get => _maxParallelism; set { if (value < 0) throw new ArgumentOutOfRangeException(nameof(MaxParallelism)); _maxParallelism = value; } }
        private int _maxParallelism = Environment.ProcessorCount;

        private CancellationTokenSource _cts = new();
        private readonly PauseTokenSource _pts = new();

        /// <summary>
        /// The main <see cref="System.Threading.CancellationToken"/> for all instances of the <see cref="IRequest"/> interface.
        /// </summary>
        public CancellationToken CancellationToken => _cts.Token;

        /// <summary>
        /// Two primary handlers to handle instances of the <see cref="IRequest"/> interface.
        /// </summary>
        public static RequestHandler[] MainRequestHandlers { get; } = new RequestHandler[] { new(), new() };

        /// <summary>
        /// A default synchronization context that targets the ThreadPool.
        /// </summary>
        public readonly SynchronizationContext DefaultSynchronizationContext = new();

        /// <summary>
        /// The number of instances of the <see cref="IRequest"/> interface that are not yet handled.
        /// </summary>
        public int Count => _requestsChannel.Count;

        /// <summary>
        /// Constructor for the <see cref="RequestHandler"/> class.
        /// </summary>
        /// <param name="requests">Instances of the <see cref="IRequest"/> interface that should be added.</param>
        public RequestHandler(params IRequest[] requests) : this()
        {
            AddRange(requests);
            _requestsChannel.Options.EasyEndToken = _pts.Token;
            _requestsChannel.Options.MaxDegreeOfParallelism = Math.Min(AutoParallelism.Invoke(), MaxParallelism);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="RequestHandler"/> class with a priority channel.
        /// If the priority count is zero, a dynamic-size priority channel is created; otherwise, a fixed-size priority channel is created.
        /// </summary>
        /// <param name="priorityCount">The number of priority levels for the fixed-size priority channel. If zero, a dynamic-size priority channel is used.</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown if the priority count is negative.</exception>
        public RequestHandler(int priorityCount = 3)
        {
            if (priorityCount < 0)
                throw new ArgumentOutOfRangeException(nameof(priorityCount), "Priority count cannot be negative.");

            _requestsChannel = priorityCount == 0 ? new DynamicPriorityChannel<IRequest>() : new FixedPriorityChannel<IRequest>(priorityCount);
        }

        /// <summary>
        /// Method to add a single instance of the <see cref="IRequest"/> interface to the handler.
        /// </summary>
        /// <param name="request">The instance of the <see cref="IRequest"/> interface that should be added.</param>
        public void Add(IRequest request)
        => _ = _requestsChannel.Writer.WriteAsync(new(request.Priority, request)).AsTask();


        /// <summary>
        /// Method to add multiple instances of the <see cref="IRequest"/> interface to the handler.
        /// </summary>
        /// <param name="requests">The instances of the <see cref="IRequest"/> interface that should be added.</param>
        public void AddRange(params IRequest[] requests)
        => Array.ForEach(requests, request => _ = _requestsChannel.Writer.WriteAsync(new(request.Priority, request)).AsTask());


        /// <summary>
        /// Method to run the instance of the <see cref="IRequest"/> interface and add instances of the <see cref="IRequest"/> interface.
        /// </summary>
        /// <param name="request">The instance of the <see cref="IRequest"/> interface that should be added.</param>
        public void RunRequests(IRequest request)
        {
            Add(request);
            RunRequests();
        }

        /// <summary>
        /// Executes the provided instances of the <see cref="IRequest"/> interface and adds them to the request queue.
        /// </summary>
        /// <param name="requests">Instances of the <see cref="IRequest"/> interface that should be added.</param>
        public void RunRequests(params IRequest[] requests)
        {
            AddRange(requests);
            RunRequests();
        }

        /// <summary>
        /// Resumes the execution of instances of the <see cref="IRequest"/> interface if the handler was previously paused.
        /// </summary>
        public void Start()
        {
            if (!_requestsChannel.Options.EasyEndToken.IsPaused)
                return;
            State = RequestState.Idle;
            _pts.Resume();
            if (Count > 0)
                RunRequests();
        }

        /// <summary>
        /// Pauses the execution of instances of the <see cref="IRequest"/> interface, allowing any currently running requests to complete.
        /// </summary>
        public void Pause()
        {
            _pts.Pause();
            State = RequestState.Paused;
        }

        /// <summary>
        /// Creates a new <see cref="CancellationTokenSource"/> if the previous one was canceled.
        /// </summary>
        public void CreateCTS()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(RequestHandler), "Cannot create a new CancellationTokenSource after the object has been disposed.");

            if (_cts.IsCancellationRequested)
            {
                _cts.Dispose();
                _cts = new CancellationTokenSource();
                _requestsChannel.Options.CancellationToken = CancellationToken;
                if (Count > 0)
                    RunRequests();
            }
        }

        /// <summary>
        /// Creates a new <see cref="CancellationTokenSource"/> for all main RequestHandlers.
        /// </summary>
        public static void CreateMainCTS() => Array.ForEach(MainRequestHandlers, handler => handler.CreateCTS());

        /// <summary>
        /// Cancels the main <see cref="CancellationTokenSource"/> for all instances of the <see cref="IRequest"/> interface in this RequestHandler.
        /// </summary>
        public void Cancel() => _cts.Cancel();

        /// <summary>
        /// Cancels the main <see cref="CancellationTokenSource"/> for all instances of the <see cref="IRequest"/> interface in the Main RequestHandlers.
        /// </summary>
        public static void CancelMainCTS() => Array.ForEach(MainRequestHandlers, handler => handler.Cancel());

        /// <summary>
        /// Pauses the execution of instances of the <see cref="IRequest"/> interface for all Main RequestHandlers, allowing any currently running requests to complete.
        /// </summary>
        public static void PauseMain() => Array.ForEach(MainRequestHandlers, handler => handler.Pause());

        /// <summary>
        /// Resumes the execution of instances of the <see cref="IRequest"/> interface for all Main RequestHandlers if they were previously paused.
        /// </summary>
        public static void ReusmeMain() => Array.ForEach(MainRequestHandlers, handler => handler.Start());


        /// <summary>
        /// This method is responsible for executing the instances of the  <see cref="IRequest"/> if the handler is not currently running.
        /// It updates the degree of parallelism based on the current system environment and runs the request channel.
        /// </summary>
        public void RunRequests()
        {
            if (State == RequestState.Running || CancellationToken.IsCancellationRequested || _pts.IsPaused)
                return;
            Task.Run(async () => await RunChannel());
        }

        /// <summary>
        /// This method is responsible for executing the instances of the <see cref="IRequest"/> if the handler is not currently running.
        /// It updates the degree of parallelism based on the current system environment and runs the request channel.
        /// </summary>
        async Task IRequest.StartRequestAsync()
        {
            if (State == RequestState.Running || CancellationToken.IsCancellationRequested || _pts.IsPaused)
                return;
            await RunChannel();
        }

        /// <summary>
        /// This method is responsible for running the request channel in parallel.
        /// </summary>
        /// <returns>async Task to await</returns>
        private async Task RunChannel()
        {
            State = RequestState.Running;
            UpdateAutoParallelism();
            await _requestsChannel.RunParallelReader(async (pair, ct) => await HandleRequests(pair));
            State = RequestState.Idle;
            if (_requestsChannel.Reader.Count != 0)
                RunRequests();
        }

        /// <summary>
        /// This method is responsible for handling a given request. It starts the request and based on the state of the request.
        /// </summary>
        /// <param name="pair">Priority request pair</param>
        /// <returns>async Task to await</returns>
        private async Task HandleRequests(PriorityItem<IRequest> pair)
        {
            IRequest request = pair.Item;
            await request.StartRequestAsync();

            if (request.State is RequestState.Compleated or RequestState.Failed or RequestState.Cancelled)
                request.Dispose();
            else if (request.State == RequestState.Idle)
                await _requestsChannel.Writer.WriteAsync(pair);
        }

        /// <summary>
        /// Sets the priority for the <see cref="RequestContainer{TRequest}"/>.
        /// Not to the contained <see cref="IRequest"/> objects.
        /// </summary>
        public void SetPriority(RequestPriority priority) => _priority = priority;

        /// <summary>
        /// Updates the degree of parallelism for executing instances of the <see cref="IRequest"/> interface based on the current system environment.
        /// </summary>
        public void UpdateAutoParallelism()
        {
            if (StaticDegreeOfParallelism == null)
                _requestsChannel.Options.MaxDegreeOfParallelism = Math.Min(AutoParallelism.Invoke(), MaxParallelism);
        }

        /// <summary>
        /// Attempts to set all <see cref="IRequest"/> objects in the container's <see cref="State"/> to idle.
        /// No new requests will be started or read while processing. And the running requests will be paused.
        /// After finishing, the request handler will still be paused.
        /// </summary>
        /// <returns>True if all <see cref="IRequest"/> objects are in an idle <see cref="RequestState"/>, otherwise false.</returns>
        public bool TrySetIdle()
        {
            Pause();
            var requests = _requestsChannel.ToArray();
            foreach (PriorityItem<IRequest> priorityItem in requests)
                _ = priorityItem.Item.TrySetIdle();
            return requests.All(x => x.Item.State == RequestState.Idle);
        }

        /// <summary>
        /// Disposes the <see cref="RequestHandler"/> instance and canceling all ongoing tasks.
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;

            _task.SetResult();
            Cancel();
            _cts.Dispose();
            _disposed = true;
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Attempts to remove the specified requests from the priority channel.
        /// </summary>
        /// <param name="requests">The requests to remove.</param>
        /// <remarks>
        /// This method can produce a significant amount of overhead, especially when dealing with a large number of requests.
        /// </remarks>
        public void Remove(params IRequest[] requests)
        {
            if (requests == null || requests.Length == 0)
                throw new ArgumentNullException(nameof(requests), "Requests cannot be null or empty.");

            foreach (var request in requests)
                if (!_requestsChannel.TryRemove(new(request.Priority, request)))
                    throw new InvalidOperationException($"Failed to remove request: {request}");
        }

        /// <summary>
        /// Returns an enumerator that iterates through the collection of requests. 
        /// Blocks the Handler for a amout of time.
        /// </summary>
        /// <returns>An enumerator that can be used to iterate through the collection.</returns>
        public IEnumerator<IRequest> GetEnumerator() => _requestsChannel.ToArray().Select(pair => pair.Item).GetEnumerator();

        /// <summary>
        /// Returns an enumerator that iterates through a collection.
        /// Blocks the Handler for a amout of time.
        /// </summary>
        /// <returns>An <see cref="System.Collections.IEnumerator"/> object that can be used to iterate through the collection.</returns>
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
