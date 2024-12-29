using Requests.Options;

namespace Requests
{
    /// <summary>
    /// Represents an <see cref="IRequest"/> object that can be managed by the <see cref="RequestHandler"/>.
    /// </summary>
    public interface IRequest : IDisposable
    {
        /// <summary>
        /// Gets the current <see cref="RequestState"/> of this <see cref="IRequest"/>.
        /// </summary>
        public abstract RequestState State { get; }

        /// <summary>
        /// Event that is triggered when the <see cref="State"/> of this object changes.
        /// </summary>
        public abstract event EventHandler<RequestState>? StateChanged;

        /// <summary>
        /// Indicates whether the <see cref="IRequest"/> has priority over other non-prioritized <see cref="IRequest">Requests</see>.
        /// </summary>
        public abstract RequestPriority Priority { get; }

        /// <summary>
        /// Specifies a request that should be executed immediately after this request completes, bypassing the queue.
        /// </summary>
        /// <remarks>
        /// The subsequent request supports auto-starting if enabled, but this behavior can be disabled if not desired.
        /// <br/>If the subsequent request is already running, it will not be started again.
        /// <br/>If this request fails, the subsequent request will be canceled and disposed.
        /// </remarks>
        public abstract IRequest? SubsequentRequest { get; }

        /// <summary>
        /// Gets the <see cref="System.Threading.Tasks.Task"/> representing the completion status of this <see cref="IRequest"/>.
        /// </summary>
        public abstract Task Task { get; }

        /// <summary>
        /// Gets the <see cref="AggregateException"/> that occurred during processing, if any.
        /// </summary>
        public abstract AggregateException? Exception { get; }

        /// <summary>
        /// Starts the execution of the <see cref="IRequest"/> created from this object.
        /// </summary>
        public Task StartRequestAsync();

        /// <summary>
        /// Cancels the execution of the <see cref="IRequest"/>.
        /// </summary>
        public abstract void Cancel();

        /// <summary>
        /// Starts the <see cref="IRequest"/> if it is not yet started or resumes it if paused.
        /// </summary>
        public abstract void Start();

        /// <summary>
        /// Puts the <see cref="IRequest"/> on hold.
        /// </summary>
        public abstract void Pause();

        /// <summary>
        /// Tries to set the <see cref="IRequest"/> <see cref="State"/> to idle.
        /// </summary>
        public abstract bool TrySetIdle();
    }
}