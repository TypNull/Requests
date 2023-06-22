using Requests.Options;

namespace Requests
{
    /// <summary>
    /// A <see cref="RequestObject"/> object that can be managed by the <see cref="RequestHandler"/>.
    /// </summary>
    public abstract class RequestObject : IDisposable
    {
        /// <summary>
        /// The <see cref="RequestState"/> of this <see cref="RequestObject"/>.
        /// </summary>
        public abstract RequestState State { get; protected set; }

        /// <summary>
        /// If the <see cref="RequestObject"/> has priority over other not prioritized <see cref="RequestObject">Requests</see>.
        /// </summary>
        public abstract RequestPriority Priority { get; }

        /// <summary>
        /// <see cref="System.Threading.Tasks.Task"/> that indicates of this <see cref="RequestObject"/> finished.
        /// </summary>
        public abstract Task Task { get; }

        /// <summary>
        /// <see cref="AggregateException"/> that occurs while processing.
        /// </summary>
        public abstract AggregateException? Exception { get; }

        /// <summary>
        /// Runs the <see cref="RequestObject"/> that was created out this object
        /// </summary>
        internal abstract Task StartRequestAsync();

        /// <summary>
        /// Cancel the <see cref="RequestObject"/>
        /// </summary>
        public abstract void Cancel();

        /// <summary>
        /// Start the <see cref="RequestObject"/> if it is not yet started or paused.
        /// </summary>
        public abstract void Start();

        /// <summary>
        /// Set the <see cref="RequestObject"/> on hold.
        /// </summary>
        public abstract void Pause();

        /// <summary>
        /// Dispose the <see cref="RequestObject"/>.
        /// </summary>
        public abstract void Dispose();

        /// <summary>
        /// Wait to finish this <see cref="RequestObject"/>.
        /// </summary>
        /// <exception cref="AggregateException"></exception>
        /// <exception cref="ObjectDisposedException"></exception>
        public virtual void Wait() => Task.Wait();
    }
}