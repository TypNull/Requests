namespace Requests.Options
{
    /// <summary>
    /// Generic interface that conatains design for all <see cref="IRequest"/> types.
    /// </summary>
    /// <typeparam name="TCompleated">Type of return if compleated</typeparam>
    /// <typeparam name="TFailed">Type of return if failed</typeparam>
    public interface IRequestOptions<TCompleated, TFailed>
    {
        /// <summary>
        /// If the Request sould be automaticly started if when it is inizialised.
        /// </summary>
        public bool AutoStart { get; set; }

        /// <summary>
        /// If the <see cref="IRequest"/> has priority over other not prioritized <see cref="IRequest">Requests</see>.
        /// </summary>
        public RequestPriority Priority { get; set; }

        /// <summary>
        /// Delays the start of the <see cref="IRequest"/> on every start call for the specified number of milliseconds.
        /// </summary>
        public TimeSpan? DeployDelay { get; set; }

        /// <summary>
        /// If the <see cref="IRequest"/> is an big file and should download in a second <see cref="Thread"/>.
        /// </summary>
        public RequestHandler Handler { get; set; }

        /// <summary>
        /// How often the <see cref="IRequest"/> should be retried if it fails.
        /// </summary>
        public byte NumberOfAttempts { get; set; }

        /// <summary>
        /// How long sould be the new attemp be delayed if the <see cref="IRequest"/> fails.
        /// </summary>
        public TimeSpan? DelayBetweenAttemps { get; set; }

        /// <summary>
        /// <see cref="System.Threading.CancellationToken"/> that the user sets to cancel the <see cref="IRequest"/>.
        /// </summary>
        public CancellationToken? CancellationToken { get; set; }

        /// <summary>
        /// Event that will be risen when the <see cref="IRequest"/> is cancelled.
        /// </summary>
        public Notify<IRequest>? RequestCancelled { get; set; }

        /// <summary>
        /// Event that will be risen when the <see cref="IRequest"/> is started.
        /// </summary>
        public Notify<IRequest>? RequestStarted { get; set; }

        /// <summary>
        /// Event that will be risen when the <see cref="IRequest"/> finished.
        /// </summary>
        public Notify<IRequest, TCompleated>? RequestCompleated { get; set; }

        /// <summary>
        /// Event that will be risen when the <see cref="IRequest"/> failed.
        /// </summary>
        public Notify<IRequest, TFailed>? RequestFailed { get; set; }
    }
}
