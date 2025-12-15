using Requests.Channel;
using Requests.Options;
using System.Runtime.CompilerServices;
using System.Text;

namespace Requests
{
    /// <summary>
    /// The <see cref="ParallelRequestHandler"/> class is responsible for executing instances of the <see cref="IRequest"/> interface in parallel.
    /// Optimized for high-performance scenarios with minimal allocations and thread-safe state management.
    /// </summary>
    public class ParallelRequestHandler : IRequestHandler, IAsyncEnumerable<IRequest>
    {
        private readonly IPriorityChannel<IRequest> _requestsChannel;
        private readonly RequestContainerStateMachine _stateMachine;
        private bool _disposed;
        private RequestPriority _priority;
        private int? _staticDegreeOfParallelism;
        private int _maxParallelism = Environment.ProcessorCount;
        private CancellationTokenSource _cts = new();
        private readonly PauseTokenSource _pts = new();
        private Task? _task;
        private Exception? _unhandledException;

        // Cached delegate to avoid allocations
        private static readonly SendOrPostCallback s_stateChangedCallback = static state =>
        {
            (ParallelRequestHandler handler, RequestState newState) = ((ParallelRequestHandler, RequestState))state!;
            handler.StateChanged?.Invoke(handler, newState);
        };

        /// <summary>
        /// Represents the current state of this <see cref="ParallelRequestHandler"/>.
        /// </summary>
        public RequestState State => _stateMachine.Current;

        /// <summary>
        /// Event triggered when the <see cref="State"/> of this object changes.
        /// </summary>
        public event EventHandler<RequestState>? StateChanged;

        /// <summary>
        /// Event triggered when an unhandled exception occurs in the handler.
        /// </summary>
        public event EventHandler<Exception>? UnhandledException;

        /// <summary>
        /// The priority of this request handler.
        /// </summary>
        public RequestPriority Priority => _priority;

        /// <summary>
        /// Represents the combined task of the requests.
        /// </summary>
        public Task Task => _task ?? Task.CompletedTask;

        /// <summary>
        /// Gets the aggregate exception associated with the <see cref="ParallelRequestHandler"/> instance.
        /// Returns the last unhandled exception if any occurred.
        /// </summary>
        public AggregateException? Exception => _unhandledException != null ? new AggregateException(_unhandledException) : null;

        /// <summary>
        /// Property that sets the degree of parallel execution of instances of the <see cref="IRequest"/> interface. 
        /// Disables AutoParallelism when set. Default value is null.
        /// </summary>
        public int? StaticDegreeOfParallelism
        {
            get => _staticDegreeOfParallelism;
            set
            {
                _requestsChannel.Options.MaxDegreeOfParallelism = value ?? AutoParallelism.Invoke();
                _staticDegreeOfParallelism = value;
            }
        }

        /// <summary>
        /// A function that calculates the degree of parallel execution of instances of the <see cref="IRequest"/> interface dynamically while running.
        /// </summary>
        public Func<int> AutoParallelism { get; set; } = () => Environment.ProcessorCount;

        /// <summary>
        /// Property that sets the maximum possible degree of parallel execution of instances of the <see cref="IRequest"/> interface.
        /// </summary>
        public int MaxParallelism
        {
            get => _maxParallelism;
            set
            {
                ArgumentOutOfRangeException.ThrowIfNegative(value);
                _maxParallelism = value;
            }
        }

        /// <summary>
        /// The main <see cref="System.Threading.CancellationToken"/> for all instances of the <see cref="IRequest"/> interface.
        /// </summary>
        public CancellationToken CancellationToken => _cts.Token;

        /// <summary>
        /// Primary handler to handle instances of the <see cref="IRequest"/> interface.
        /// </summary>
        public static ParallelRequestHandler MainRequestHandler { get; } = [];

        /// <summary>
        /// A default synchronization context that targets the ThreadPool.
        /// </summary>
        public SynchronizationContext DefaultSynchronizationContext { get; } = new();

        /// <summary>
        /// The number of instances of the <see cref="IRequest"/> interface that are not yet handled.
        /// </summary>
        public int Count => _requestsChannel.Count;

        /// <summary>
        /// Asynchronously enumerates all currently pending requests in the handler.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token to stop enumeration.</param>
        /// <returns>An async enumerator of pending requests.</returns>
        /// <remarks>
        /// <strong>Warning:</strong> This operation may block the handler for a period of time.
        /// </remarks>
        public async IAsyncEnumerator<IRequest> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            // Take a snapshot to avoid blocking
            PriorityItem<IRequest>[] snapshot;
            try
            {
                snapshot = _requestsChannel.ToArray();
            }
            catch
            {
                yield break;
            }

            foreach (PriorityItem<IRequest> item in snapshot)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return item.Item;
                await Task.Yield();
            }
        }

        /// <summary>
        /// Waits for all currently pending requests to complete.
        /// Equivalent to awaiting all requests from 'await foreach (var request in handler)'.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Task that completes when all current requests are done.</returns>
        /// <remarks>
        /// <strong>Warning:</strong> This operation may block the handler for a period of time.
        /// </remarks>
        public async Task WaitForCurrentRequestsAsync(CancellationToken cancellationToken = default)
        {
            List<Task> tasks = [];

            await foreach (IRequest request in this.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                tasks.Add(request.Task);
            }

            if (tasks.Count > 0)
                await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        /// <summary>
        /// Specifies a request that should be executed immediately after this request completes, bypassing the queue.
        /// </summary>
        IRequest? IRequest.SubsequentRequest => null;

        /// <summary>
        /// Initializes a new instance of the <see cref="ParallelRequestHandler"/> class with a priority channel.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">Thrown if the priority count is negative.</exception>
        public ParallelRequestHandler()
        {
            _requestsChannel = new DynamicPriorityChannel<IRequest>();
            _stateMachine = new RequestContainerStateMachine(RequestState.Idle, OnStateChanged);
        }

        /// <summary>
        /// Constructor for the <see cref="ParallelRequestHandler"/> class.
        /// </summary>
        /// <param name="requests">Instances of the <see cref="IRequest"/> interface that should be added.</param>
        public ParallelRequestHandler(params IRequest[] requests) : this()
        {
            AddRange(requests);
            _requestsChannel.Options.EasyEndToken = _pts.Token;
            _requestsChannel.Options.MaxDegreeOfParallelism = Math.Min(AutoParallelism.Invoke(), MaxParallelism);
        }

        /// <summary>
        /// Callback when state changes.
        /// </summary>
        private void OnStateChanged(RequestState oldState, RequestState newState)
            => DefaultSynchronizationContext.Post(s_stateChangedCallback, (this, newState));

        /// <summary>
        /// Handles unhandled exceptions from the handler's execution.
        /// </summary>
        private void OnUnhandledExceptionOccurred(Exception ex)
        {
            _unhandledException = ex;
            DefaultSynchronizationContext.Post(static state =>
            {
                (ParallelRequestHandler handler, Exception exception) = ((ParallelRequestHandler, Exception))state!;
                handler.UnhandledException?.Invoke(handler, exception);
            }, (this, ex));
        }

        /// <summary>
        /// Synchronously adds a request to the handler and starts processing if not already running.
        /// Throws if the channel is closed or the request is null.
        /// </summary>
        /// <param name="request">The instance of the <see cref="IRequest"/> interface that should be added.</param>
        /// <exception cref="ArgumentNullException">Thrown if request is null.</exception>
        /// <exception cref="InvalidOperationException">Thrown if the channel is closed.</exception>
        public void Add(IRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);

            if (!_requestsChannel.Writer.TryWrite(new(request.Priority, request)))
                throw new InvalidOperationException("Failed to add request, channel may be closed or full");

            RunRequests();
        }

        /// <summary>
        /// Asynchronously adds a request to the handler.
        /// </summary>
        /// <param name="request">The request to add.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async ValueTask AddAsync(IRequest request, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            await _requestsChannel.Writer.WriteAsync(new(request.Priority, request), cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Method to add multiple instances of the <see cref="IRequest"/> interface to the handler.
        /// </summary>
        /// <param name="requests">The instances of the <see cref="IRequest"/> interface that should be added.</param>
        public void AddRange(params IRequest[] requests)
        {
            ArgumentNullException.ThrowIfNull(requests);

            bool anyAdded = false;
            foreach (IRequest request in requests)
            {
                ArgumentNullException.ThrowIfNull(request);
                if (_requestsChannel.Writer.TryWrite(new(request.Priority, request)))
                    anyAdded = true;
                else
                    throw new InvalidOperationException("Failed to add request, channel may be closed or full");
            }

            if (anyAdded)
                RunRequests();
        }

        /// <summary>
        /// Resumes the execution of instances of the <see cref="IRequest"/> interface if the handler was previously paused.
        /// </summary>
        public void Start()
        {
            if (!_pts.IsPaused)
                return;

            if (!_stateMachine.TryTransition(RequestState.Idle))
                return;

            _pts.Resume();

            if (_requestsChannel.Reader.Count > 0)
                RunRequests();
        }

        /// <summary>
        /// Pauses the execution of instances of the <see cref="IRequest"/> interface, allowing any currently running requests to complete.
        /// </summary>
        public void Pause()
        {
            _pts.Pause();
            _stateMachine.TryTransition(RequestState.Paused);
        }

        /// <summary>
        /// Creates a new <see cref="CancellationTokenSource"/> if the previous one was canceled.
        /// </summary>
        public void CreateCTS()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ParallelRequestHandler), "Cannot create a new CancellationTokenSource after the object has been disposed.");

            if (_cts.IsCancellationRequested)
            {
                _cts.Dispose();
                _cts = new CancellationTokenSource();
                _requestsChannel.Options.CancellationToken = CancellationToken;

                _stateMachine.TryTransition(RequestState.Idle);
                if (Count > 0)
                    RunRequests();
            }
        }

        /// <summary>
        /// Cancels the main <see cref="CancellationTokenSource"/> for all instances of the <see cref="IRequest"/> interface in this RequestHandler.
        /// </summary>
        public void Cancel()
        {
            _cts.Cancel();
            _stateMachine.TryTransition(RequestState.Cancelled);
        }

        /// <summary>
        /// This method is responsible for executing the instances of the <see cref="IRequest"/> if the handler is not currently running.
        /// It updates the degree of parallelism based on the current system environment and runs the request channel.
        /// </summary>
        public void RunRequests()
        {
            if (State != RequestState.Idle)
                return;

            _task = Task.Run(async () =>
            {
                try
                {
                    await ((IRequest)this).StartRequestAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    OnUnhandledExceptionOccurred(ex);
                }
            });
        }

        /// <summary>
        /// This method is responsible for executing the instances of the <see cref="IRequest"/> if the handler is not currently running.
        /// It updates the degree of parallelism based on the current system environment and runs the request channel.
        /// </summary>
        async Task IRequest.StartRequestAsync()
        {
            if (!_stateMachine.TryTransition(RequestState.Running) || CancellationToken.IsCancellationRequested || _pts.IsPaused)
                return;

            await RunChannelAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// This method is responsible for running the request channel in parallel.
        /// </summary>
        /// <returns>async Task to await</returns>
        private async Task RunChannelAsync()
        {
            UpdateAutoParallelism();

            try
            {
                await _requestsChannel.RunParallelReader(async (pair, ct) =>
                    await HandleRequestAsync(pair).ConfigureAwait(false))
                    .ConfigureAwait(false);
            }
            finally
            {
                _stateMachine.TryTransition(RequestState.Idle);
            }

            if (_requestsChannel.Reader.Count > 0)
                await ((IRequest)this).StartRequestAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// This method is responsible for handling a given request. It starts the request and based on the state of the request.
        /// </summary>
        /// <param name="pair">Priority request pair</param>
        /// <returns>async Task to await</returns>
        private async Task HandleRequestAsync(PriorityItem<IRequest> pair)
        {
            IRequest request = pair.Item;
            await request.StartRequestAsync().ConfigureAwait(false);

            if (request.State is RequestState.Completed or RequestState.Failed or RequestState.Cancelled)
            {
                request.Dispose();
                if (request.SubsequentRequest != null)
                    await HandleSubsequentRequestAsync(request).ConfigureAwait(false);
            }
            else if (request.State == RequestState.Idle)
            {
                await _requestsChannel.Writer.WriteAsync(pair, CancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Processes the subsequent request of the given request. Starts it if the current request completes,
        /// or disposes it and processes the chain if the current request fails or is canceled.
        /// </summary>
        /// <param name="request">The request to process.</param>
        /// <returns>A task representing the operation.</returns>
        private async Task HandleSubsequentRequestAsync(IRequest request)
        {
            IRequest subRequest = request.SubsequentRequest!;

            if (request.State == RequestState.Completed)
            {
                if (subRequest.State != RequestState.Running && subRequest.TrySetIdle())
                    await HandleRequestAsync(new PriorityItem<IRequest>(subRequest.Priority, subRequest)).ConfigureAwait(false);
            }
            else
            {
                subRequest.Dispose();
                if (subRequest.SubsequentRequest != null)
                    await HandleSubsequentRequestAsync(subRequest).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Sets the priority for the <see cref="ParallelRequestHandler"/>.
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
        /// Pauses the handler during this operation and returns it to the previous state afterward.
        /// </summary>
        /// <returns>True if all <see cref="IRequest"/> objects are in an idle <see cref="RequestState"/>, otherwise false.</returns>
        public bool TrySetIdle()
        {
            if (State != RequestState.Paused)
                return false;

            PriorityItem<IRequest>[] requests = _requestsChannel.ToArray();

            foreach (PriorityItem<IRequest> priorityItem in requests)
                _ = priorityItem.Item.TrySetIdle();

            bool allIdle = requests.All(x => x.Item.State == RequestState.Idle);
            return allIdle;
        }

        /// <summary>
        /// Checks whether the <see cref="ParallelRequestHandler"/> has completed all work.
        /// </summary>
        /// <returns><c>true</c> if the handler is in a terminal state and has no pending requests; otherwise, <c>false</c>.</returns>
        public bool HasCompleted() =>
            State is RequestState.Completed or RequestState.Cancelled
            && _requestsChannel.Reader.Count == 0;

        /// <summary>
        /// Yield point for IRequest interface compatibility.
        /// Handlers don't yield in the same way as individual requests, this is a no-op.
        /// </summary>
        /// <returns>A completed ValueTask.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ValueTask YieldAsync() => ValueTask.CompletedTask;

        /// <summary>
        /// Disposes the <see cref="ParallelRequestHandler"/> instance and canceling all ongoing tasks.
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;

            Cancel();
            _requestsChannel.Writer.TryComplete();
            _cts.Dispose();
            _disposed = true;
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Provides a detailed string representation of the current state of the <see cref="ParallelRequestHandler"/> instance.
        /// </summary>
        /// <returns>A string that represents the current state of the <see cref="ParallelRequestHandler"/>.</returns>
        public override string ToString()
        {
            StringBuilder sb = new();

            sb.AppendLine("RequestHandler State:");
            sb.AppendLine($"  Disposed: {_disposed}");
            sb.AppendLine($"  Cancellation Requested: {_cts.IsCancellationRequested}");
            sb.AppendLine($"  Paused: {_pts.IsPaused}");
            sb.AppendLine($"  State: {State}");
            sb.AppendLine($"  Priority: {Priority}");
            sb.AppendLine($"  Task Status: {Task.Status}");
            sb.AppendLine($"  Exception: {Exception?.Message ?? "None"}");
            sb.AppendLine($"  Static Degree of Parallelism: {StaticDegreeOfParallelism?.ToString() ?? "Auto"}");
            sb.AppendLine($"  Max Parallelism: {MaxParallelism}");
            sb.AppendLine($"  Request Count: {Count}");
            sb.AppendLine($"  CancellationToken: {CancellationToken.IsCancellationRequested}");
            sb.AppendLine($"  PauseToken: {_pts.IsPaused}");

            return sb.ToString();
        }

        /// <summary>
        /// Attempts to remove the specified requests from the priority channel.
        /// </summary>
        /// <param name="requests">The requests to remove.</param>
        /// <exception cref="ArgumentNullException">Thrown if requests is null or empty.</exception>
        /// <exception cref="InvalidOperationException">Thrown if a request cannot be removed.</exception>
        /// <remarks>
        /// <strong>Warning:</strong> This method can produce a significant amount of overhead, especially when dealing with a large number of requests.
        /// </remarks>
        public void Remove(params IRequest[] requests)
        {
            if (requests == null || requests.Length == 0)
                throw new ArgumentNullException(nameof(requests), "Requests cannot be null or empty.");

            foreach (IRequest request in requests)
            {
                if (request == null)
                    throw new ArgumentNullException(nameof(requests), "Individual request cannot be null.");

                if (!_requestsChannel.TryRemove(new(request.Priority, request)))
                    throw new InvalidOperationException($"Failed to remove request: {request}");
            }
        }

        /// <summary>
        /// Returns an enumerator that iterates through the collection of requests.
        /// <remarks>
        /// <strong>Warning:</strong> This operation may block the handler for a period of time.
        /// </remarks>
        /// </summary>
        /// <returns>An enumerator that can be used to iterate through the collection of requests.</returns>
        public IEnumerator<IRequest> GetEnumerator() => _requestsChannel.ToArray().Select(pair => pair.Item).GetEnumerator();

        /// <summary>
        /// Returns an enumerator that iterates through the collection of requests.
        /// <remarks>
        /// <strong>Warning:</strong> This operation may block the handler for a period of time.
        /// </remarks>
        /// </summary>
        /// <returns>An <see cref="System.Collections.IEnumerator"/> object that can be used to iterate through the collection.</returns>
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}