namespace Requests.Options
{
    /// <summary>
    /// A record that implements the <see cref="IRequestOptions"/> interface.
    /// Provides configuration for request execution behavior.
    /// </summary>
    public record RequestOptions : IRequestOptions
    {
        private IRequest? _subsequentRequest;

        /// <inheritdoc />
        public bool AutoStart { get; init; } = true;

        /// <inheritdoc />
        public RequestPriority Priority { get; init; } = RequestPriority.Normal;

        /// <inheritdoc />
        public CancellationToken CancellationToken { get; init; }

        /// <inheritdoc />
        public TimeSpan? DeployDelay { get; set; }

        /// <inheritdoc />
        public IRequestHandler? Handler { get; init; } = ParallelRequestHandler.MainRequestHandler;

        /// <inheritdoc />
        public byte NumberOfAttempts { get; init; } = 3;

        /// <inheritdoc />
        public TimeSpan? DelayBetweenAttempts { get; init; }

        /// <inheritdoc />
        public IRequest? SubsequentRequest
        {
            get => _subsequentRequest;
            set
            {
                if (value?.HasCompleted() == true)
                    throw new ArgumentException("Cannot set a completed request as subsequent request.", nameof(value));

                _subsequentRequest = value;
            }
        }
    }
}
