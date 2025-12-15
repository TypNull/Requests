namespace Requests.Options
{
    /// <summary>
    /// A generic interface that defines the structure for all <see cref="IRequest"/> types.
    /// </summary>
    /// <typeparam name="TCompleted">The return type if the request is completed successfully.</typeparam>
    /// <typeparam name="TFailed">The return type if the request fails.</typeparam>
    public interface IRequestOptions<TCompleted, TFailed>
    {
        /// <summary>
        /// Determines whether the <see cref="IRequest"/> should be automatically started upon initialization.
        /// </summary>
        public bool AutoStart { get; set; }

        /// <summary>
        /// Specifies if the <see cref="IRequest"/> has priority over other non-prioritized <see cref="IRequest">Requests</see>.
        /// </summary>
        public RequestPriority Priority { get; set; }

        /// <summary>
        /// Delays the start of the <see cref="IRequest"/> by a specified number of milliseconds on every start call.
        /// </summary>
        public TimeSpan? DeployDelay { get; set; }

        /// <summary>
        /// Sets the <see cref="IRequestHandler"/> for the <see cref="IRequest"/>.
        /// </summary>
        public IRequestHandler? Handler { get; set; }

        /// <summary>
        /// Specifies the number of times the <see cref="IRequest"/> should be retried if it fails.
        /// </summary>
        public byte NumberOfAttempts { get; set; }

        /// <summary>
        /// Specifies the delay duration before a new attempt is made if the <see cref="IRequest"/> fails.
        /// </summary>
        public TimeSpan? DelayBetweenAttemps { get; set; }

        /// <summary>
        /// A <see cref="System.Threading.CancellationToken"/> that the user can set to cancel the <see cref="IRequest"/>.
        /// </summary>
        public CancellationToken? CancellationToken { get; set; }

        /// <summary>
        /// Specifies a request that should be executed immediately after this request completes, bypassing the queue.
        /// </summary>
        /// <remarks>
        /// The subsequent request supports auto-starting if enabled, but this behavior can be disabled if not desired.
        /// <br/>If the subsequent request is already running, it will not be started again.
        /// <br/>If the holding request fails, the subsequent request will be canceled and disposed.
        /// </remarks>
        public IRequest? SubsequentRequest { get; set; }

        /// <summary>
        /// An event that will be triggered when the <see cref="IRequest"/> is cancelled.
        /// </summary>
        public Action<IRequest>? RequestCancelled { get; set; }

        /// <summary>
        /// An event that will be triggered when the <see cref="IRequest"/> is started.
        /// </summary>
        public Action<IRequest>? RequestStarted { get; set; }

        /// <summary>
        /// An event that will be triggered when the <see cref="IRequest"/> is completed.
        /// </summary>
        public Action<IRequest, TCompleted>? RequestCompleted { get; set; }

        /// <summary>
        /// An event that will be triggered when the <see cref="IRequest"/> fails.
        /// </summary>
        public Action<IRequest, TFailed>? RequestFailed { get; set; }

        /// <summary>
        /// An event that will be triggered when an exception occurs during the <see cref="IRequest"/> execution.
        /// </summary>
        public Action<IRequest, Exception>? RequestExceptionOccurred { get; set; }
    }
}
