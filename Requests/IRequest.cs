using Requests.Options;

namespace Requests
{
    /// <summary>
    /// A <see cref="IRequest"/> object that can be managed by the <see cref="RequestHandler"/>.
    /// </summary>
    public interface IRequest : IDisposable
    {
        /// <summary>
        /// The <see cref="RequestState"/> of this <see cref="IRequest"/>.
        /// </summary>
        public abstract RequestState State { get; }

        /// <summary>
        /// Event that will be invoked when the <see cref="State"/> of this object changed.
        /// </summary>
        public abstract event EventHandler<RequestState>? StateChanged;

        /// <summary>
        /// If the <see cref="IRequest"/> has priority over other not prioritized <see cref="IRequest">Requests</see>.
        /// </summary>
        public abstract RequestPriority Priority { get; }

        /// <summary>
        /// <see cref="System.Threading.Tasks.Task"/> that indicates of this <see cref="IRequest"/> finished.
        /// </summary>
        public abstract Task Task { get; }

        /// <summary>
        /// <see cref="AggregateException"/> that occurs while processing.
        /// </summary>
        public abstract AggregateException? Exception { get; }

        /// <summary>
        /// Runs the <see cref="IRequest"/> that was created out this object
        /// </summary>
        protected internal Task StartRequestAsync();

        /// <summary>
        /// Cancel the <see cref="IRequest"/>
        /// </summary>
        public abstract void Cancel();

        /// <summary>
        /// Start the <see cref="IRequest"/> if it is not yet started or paused.
        /// </summary>
        public abstract void Start();

        /// <summary>
        /// Set the <see cref="IRequest"/> on hold.
        /// </summary>
        public abstract void Pause();
    }
}