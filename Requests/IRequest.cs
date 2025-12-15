using Requests.Options;

namespace Requests
{
    /// <summary>
    /// Represents a request that can be managed by the <see cref="ParallelRequestHandler"/>.
    /// </summary>
    public interface IRequest : IDisposable
    {
        /// <summary>
        /// Gets the current <see cref="RequestState"/> of this request.
        /// </summary>
        RequestState State { get; }

        /// <summary>
        /// Event that is triggered when the <see cref="State"/> changes.
        /// </summary>
        event EventHandler<RequestState>? StateChanged;

        /// <summary>
        /// Gets the priority level of this request.
        /// Higher priority requests are executed before lower priority ones.
        /// </summary>
        RequestPriority Priority { get; }

        /// <summary>
        /// Gets the request that should be executed immediately after this request completes,
        /// bypassing the queue.
        /// </summary>
        /// <remarks>
        /// The subsequent request supports auto-starting if enabled.
        /// If the subsequent request is already running, it will not be started again.
        /// If this request fails, the subsequent request will be canceled and disposed.
        /// </remarks>
        IRequest? SubsequentRequest { get; }

        /// <summary>
        /// Gets the <see cref="System.Threading.Tasks.Task"/> representing the 
        /// completion status of this request.
        /// </summary>
        Task Task { get; }

        /// <summary>
        /// Gets the <see cref="AggregateException"/> that occurred during processing, if any.
        /// </summary>
        AggregateException? Exception { get; }

        /// <summary>
        /// Starts the execution of the request asynchronously.
        /// </summary>
        /// <returns>A task representing the asynchronous operation.</returns>
        Task StartRequestAsync();

        /// <summary>
        /// Cancels the execution of the request.
        /// </summary>
        void Cancel();

        /// <summary>
        /// Starts the request if it is not yet started, or resumes it if paused.
        /// </summary>
        void Start();

        /// <summary>
        /// Pauses the request, allowing it to be resumed later.
        /// </summary>
        void Pause();

        /// <summary>
        /// Attempts to set the request <see cref="State"/> to idle.
        /// </summary>
        /// <returns><c>true</c> if the state was successfully set to idle; otherwise, <c>false</c>.</returns>
        bool TrySetIdle();

        /// <summary>
        /// Checks whether the request has reached a final state and will no longer change.
        /// </summary>
        /// <returns><c>true</c> if the request is in a final state; otherwise, <c>false</c>.</returns>
        bool HasCompleted();

        /// <summary>
        /// Yields control back to the request scheduler, allowing inline pause.
        /// </summary>
        /// <returns>A task that completes when the request can continue execution.</returns>
        ValueTask YieldAsync();
    }
}