using Requests.Channel;
using Requests.Options;
using System.Runtime.CompilerServices;
using System.Text;

namespace Requests;

/// <summary>
/// The <see cref="SequentialRequestHandler"/> class is responsible for executing instances of the <see cref="IRequest"/> interface sequentially (one at a time).
/// </summary>
public class SequentialRequestHandler : IRequestHandler, IAsyncEnumerable<IRequest>
{
    private readonly ConcurrentPriorityQueue<IRequest> _requestQueue;
    private readonly RequestContainerStateMachine _stateMachine;
    private bool _disposed;
    private RequestPriority _priority;
    private CancellationTokenSource _cts = new();
    private readonly PauseTokenSource _pts = new();
    private Task? _task;
    private Exception? _unhandledException;

    // Cached delegate to avoid allocations
    private static readonly SendOrPostCallback s_stateChangedCallback = static state =>
    {
        (SequentialRequestHandler handler, RequestState newState) = ((SequentialRequestHandler, RequestState))state!;
        handler.StateChanged?.Invoke(handler, newState);
    };

    /// <summary>
    /// Represents the current state of this <see cref="SequentialRequestHandler"/>.
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
    /// Gets the aggregate exception associated with the <see cref="SequentialRequestHandler"/> instance.
    /// Returns the last unhandled exception if any occurred.
    /// </summary>
    public AggregateException? Exception => _unhandledException != null ? new AggregateException(_unhandledException) : null;

    /// <summary>
    /// The main <see cref="System.Threading.CancellationToken"/> for all instances of the <see cref="IRequest"/> interface.
    /// </summary>
    public CancellationToken CancellationToken => _cts.Token;

    /// <summary>
    /// A default synchronization context that targets the ThreadPool.
    /// </summary>
    public SynchronizationContext DefaultSynchronizationContext { get; } = new();

    /// <summary>
    /// The number of instances of the <see cref="IRequest"/> interface that are not yet handled.
    /// </summary>
    public int Count => _requestQueue.Count;

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
            snapshot = [.. _requestQueue];
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
    /// Initializes a new instance of the <see cref="SequentialRequestHandler"/> class.
    /// </summary>
    public SequentialRequestHandler()
    {
        _requestQueue = new ConcurrentPriorityQueue<IRequest>();
        _stateMachine = new RequestContainerStateMachine(RequestState.Idle, OnStateChanged);
    }

    /// <summary>
    /// Constructor for the <see cref="SequentialRequestHandler"/> class.
    /// </summary>
    /// <param name="requests">Instances of the <see cref="IRequest"/> interface that should be added.</param>
    public SequentialRequestHandler(params IRequest[] requests) : this()
    {
        AddRange(requests);
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
            (SequentialRequestHandler handler, Exception exception) = ((SequentialRequestHandler, Exception))state!;
            handler.UnhandledException?.Invoke(handler, exception);
        }, (this, ex));
    }

    /// <summary>
    /// Synchronously adds a request to the handler and starts processing if not already running.
    /// Throws if the request is null.
    /// </summary>
    /// <param name="request">The instance of the <see cref="IRequest"/> interface that should be added.</param>
    /// <exception cref="ArgumentNullException">Thrown if request is null.</exception>
    public void Add(IRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        _requestQueue.Enqueue(new PriorityItem<IRequest>(request.Priority, request));
        RunRequests();
    }

    /// <summary>
    /// Asynchronously adds a request to the handler.
    /// </summary>
    /// <param name="request">The request to add.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public ValueTask AddAsync(IRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        _requestQueue.Enqueue(new PriorityItem<IRequest>(request.Priority, request));
        RunRequests();
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Method to add multiple instances of the <see cref="IRequest"/> interface to the handler.
    /// </summary>
    /// <param name="requests">The instances of the <see cref="IRequest"/> interface that should be added.</param>
    public void AddRange(params IRequest[] requests)
    {
        ArgumentNullException.ThrowIfNull(requests);

        foreach (IRequest request in requests)
        {
            ArgumentNullException.ThrowIfNull(request);
            _requestQueue.Enqueue(new PriorityItem<IRequest>(request.Priority, request));
        }

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

        if (_requestQueue.Count > 0)
            RunRequests();
    }

    /// <summary>
    /// Pauses the execution of instances of the <see cref="IRequest"/> interface, allowing the currently running request to complete.
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
            throw new ObjectDisposedException(nameof(SequentialRequestHandler), "Cannot create a new CancellationTokenSource after the object has been disposed.");

        if (_cts.IsCancellationRequested)
        {
            _cts.Dispose();
            _cts = new CancellationTokenSource();

            _stateMachine.TryTransition(RequestState.Idle);
            if (Count > 0)
                RunRequests();
        }
    }

    /// <summary>
    /// Cancels the main <see cref="CancellationTokenSource"/> for all instances of the <see cref="IRequest"/> interface in this SequentialRequestHandler.
    /// </summary>
    public void Cancel()
    {
        _cts.Cancel();
        _stateMachine.TryTransition(RequestState.Cancelled);
    }

    /// <summary>
    /// This method is responsible for executing the instances of the <see cref="IRequest"/> if the handler is not currently running.
    /// Requests are processed sequentially, one at a time.
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
    /// This method is responsible for executing the instances of the <see cref="IRequest"/> sequentially if the handler is not currently running.
    /// </summary>
    async Task IRequest.StartRequestAsync()
    {
        if (!_stateMachine.TryTransition(RequestState.Running) || CancellationToken.IsCancellationRequested || _pts.IsPaused)
            return;

        await RunQueueSequentiallyAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// This method is responsible for running the request queue sequentially (one request at a time).
    /// </summary>
    /// <returns>async Task to await</returns>
    private async Task RunQueueSequentiallyAsync()
    {
        try
        {
            // Process requests one at a time
            while (!CancellationToken.IsCancellationRequested && !_pts.IsPaused)
            {
                // Try to get the next request
                if (!_requestQueue.TryDequeue(out PriorityItem<IRequest> pair))
                    break; // No more requests

                // Process this request sequentially
                await HandleRequestAsync(pair).ConfigureAwait(false);

                // Check pause/cancel between requests
                if (CancellationToken.IsCancellationRequested || _pts.IsPaused)
                    break;
            }
        }
        finally
        {
            _stateMachine.TryTransition(RequestState.Idle);
        }

        // Check if there are more requests to process
        if (_requestQueue.Count > 0 && !CancellationToken.IsCancellationRequested && !_pts.IsPaused)
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
            // Re-queue the request for later processing
            _requestQueue.Enqueue(pair);
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
    /// Sets the priority for the <see cref="SequentialRequestHandler"/>.
    /// Does not affect the priority of contained <see cref="IRequest"/> objects.
    /// </summary>
    public void SetPriority(RequestPriority priority) => _priority = priority;

    /// <summary>
    /// Attempts to set all <see cref="IRequest"/> objects in the container's <see cref="State"/> to idle.
    /// Pauses the handler during this operation and returns it to the previous state afterward.
    /// </summary>
    /// <returns>True if all <see cref="IRequest"/> objects are in an idle <see cref="RequestState"/>, otherwise false.</returns>
    public bool TrySetIdle()
    {
        RequestState previousState = State;

        if (!_stateMachine.TryTransition(RequestState.Paused))
            return false;

        try
        {
            PriorityItem<IRequest>[] requests = _requestQueue.ToArray();

            foreach (PriorityItem<IRequest> priorityItem in requests)
                _ = priorityItem.Item.TrySetIdle();

            bool allIdle = requests.All(x => x.Item.State == RequestState.Idle);

            // Restore previous state if we paused it
            if (previousState != RequestState.Paused)
                _stateMachine.TryTransition(previousState);

            return allIdle;
        }
        catch
        {
            // Restore state on error
            if (previousState != RequestState.Paused)
                _stateMachine.TryTransition(previousState);
            throw;
        }
    }

    /// <summary>
    /// Checks whether the <see cref="SequentialRequestHandler"/> has completed all work.
    /// </summary>
    /// <returns><c>true</c> if the handler is in a terminal state and has no pending requests; otherwise, <c>false</c>.</returns>
    public bool HasCompleted() =>
        State is RequestState.Completed or RequestState.Cancelled
        && _requestQueue.Count == 0;

    /// <summary>
    /// Yield point for IRequest interface compatibility.
    /// Handlers don't yield in the same way as individual requests, this is a no-op.
    /// </summary>
    /// <returns>A completed ValueTask.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask YieldAsync() => ValueTask.CompletedTask;

    /// <summary>
    /// Disposes the <see cref="SequentialRequestHandler"/> instance and cancels all ongoing tasks.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        Cancel();
        _cts.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Provides a detailed string representation of the current state of the <see cref="SequentialRequestHandler"/> instance.
    /// </summary>
    /// <returns>A string that represents the current state of the <see cref="SequentialRequestHandler"/>.</returns>
    public override string ToString()
    {
        StringBuilder sb = new();

        sb.AppendLine("SequentialRequestHandler State:");
        sb.AppendLine($"  Disposed: {_disposed}");
        sb.AppendLine($"  Cancellation Requested: {_cts.IsCancellationRequested}");
        sb.AppendLine($"  Paused: {_pts.IsPaused}");
        sb.AppendLine($"  State: {State}");
        sb.AppendLine($"  Priority: {Priority}");
        sb.AppendLine($"  Task Status: {Task.Status}");
        sb.AppendLine($"  Exception: {Exception?.Message ?? "None"}");
        sb.AppendLine($"  Request Count: {Count}");
        sb.AppendLine($"  CancellationToken: {CancellationToken.IsCancellationRequested}");
        sb.AppendLine($"  PauseToken: {_pts.IsPaused}");

        return sb.ToString();
    }

    /// <summary>
    /// Attempts to remove the specified requests from the priority queue.
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

            if (!_requestQueue.TryRemove(new PriorityItem<IRequest>(request.Priority, request)))
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
    public IEnumerator<IRequest> GetEnumerator() => _requestQueue.ToArray().Select(pair => pair.Item).GetEnumerator();

    /// <summary>
    /// Returns an enumerator that iterates through the collection of requests.
    /// <remarks>
    /// <strong>Warning:</strong> This operation may block the handler for a period of time.
    /// </remarks>
    /// </summary>
    /// <returns>An <see cref="System.Collections.IEnumerator"/> object that can be used to iterate through the collection.</returns>
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}
