namespace Requests.Options
{
    /// <summary>
    /// Defines the configuration options for a request.
    /// </summary>
    public interface IRequestOptions
    {
        /// <summary>
        /// Determines whether the <see cref="IRequest"/> should be automatically started upon initialization.
        /// </summary>
        bool AutoStart { get; init; }

        /// <summary>
        /// Specifies if the <see cref="IRequest"/> has priority over other non-prioritized <see cref="IRequest">Requests</see>.
        /// </summary>
        RequestPriority Priority { get; init; }

        /// <summary>
        /// Delays the start of the <see cref="IRequest"/> by a specified duration on every start call.
        /// Mutable at runtime.
        /// </summary>
        TimeSpan? DeployDelay { get; set; }

        /// <summary>
        /// Sets the <see cref="IRequestHandler"/> for the <see cref="IRequest"/>.
        /// </summary>
        IRequestHandler? Handler { get; init; }

        /// <summary>
        /// Specifies the number of times the <see cref="IRequest"/> should be retried if it fails.
        /// </summary>
        byte NumberOfAttempts { get; init; }

        /// <summary>
        /// Specifies the delay duration before a new attempt is made if the <see cref="IRequest"/> fails.
        /// </summary>
        TimeSpan? DelayBetweenAttempts { get; init; }

        /// <summary>
        /// A <see cref="System.Threading.CancellationToken"/> that the user can set to cancel the <see cref="IRequest"/>.
        /// </summary>
        CancellationToken CancellationToken { get; init; }

        /// <summary>
        /// Specifies a request that should be executed immediately after this request completes, bypassing the queue.
        /// Mutable at runtime with validation.
        /// </summary>
        /// <remarks>
        /// The subsequent request supports auto-starting if enabled, but this behavior can be disabled if not desired.
        /// <br/>If the subsequent request is already running, it will not be started again.
        /// <br/>If the holding request fails, the subsequent request will be canceled and disposed.
        /// </remarks>
        IRequest? SubsequentRequest { get; set; }
    }
}
