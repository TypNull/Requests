namespace Requests.Options
{
    /// <summary>
    /// A record that implements the <see cref="IRequestOptions{TCompleted, TFailed}"/> interface.
    /// </summary>
    /// <typeparam name="TCompleted">The return type if the request is completed successfully.</typeparam>
    /// <typeparam name="TFailed">The return type if the request fails.</typeparam>
    public record RequestOptions<TCompleted, TFailed> : IRequestOptions<TCompleted, TFailed>
    {
        /// <inheritdoc />
        public bool AutoStart { get; set; } = true;

        ///<inheritdoc />
        public RequestPriority Priority { get; set; } = RequestPriority.Normal;

        ///<inheritdoc />
        public CancellationToken? CancellationToken { get; set; }

        ///<inheritdoc />
        public TimeSpan? DeployDelay { get; set; } = null;

        ///<inheritdoc />
        public RequestHandler Handler { get; set; } = RequestHandler.MainRequestHandlers[0];

        ///<inheritdoc />
        public byte NumberOfAttempts { get; set; } = 3;

        ///<inheritdoc />
        public TimeSpan? DelayBetweenAttemps { get; set; } = null;

        ///<inheritdoc />
        public IRequest? SubsequentRequest { get; set; }

        ///<inheritdoc />
        public Action<IRequest>? RequestStarted { get; set; }

        ///<inheritdoc />
        public Action<IRequest, TCompleted>? RequestCompleted { get; set; }

        ///<inheritdoc />
        public Action<IRequest, TFailed>? RequestFailed { get; set; }

        ///<inheritdoc />
        public Action<IRequest>? RequestCancelled { get; set; }

        ///<inheritdoc />
        public Action<IRequest, Exception>? RequestExceptionOccurred { get; set; }

        /// <summary>
        /// Copy constructor for the RequestOptions record.
        /// </summary>
        /// <param name="options">The RequestOptions instance to copy from.</param>
        protected RequestOptions(RequestOptions<TCompleted, TFailed> options)
        {
            Priority = options.Priority;
            Handler = options.Handler;
            NumberOfAttempts = options.NumberOfAttempts;
            CancellationToken = options.CancellationToken;
            AutoStart = options.AutoStart;
            DelayBetweenAttemps = options.DelayBetweenAttemps;
            DeployDelay = options.DeployDelay;
            SubsequentRequest = options.SubsequentRequest;
            RequestCancelled += options.RequestCancelled;
            RequestStarted += options.RequestStarted;
            RequestFailed += options.RequestFailed;
            RequestCompleted += options.RequestCompleted;
            RequestExceptionOccurred += options.RequestExceptionOccurred;
        }

        /// <summary>
        /// Default constructor for the RequestOptions record.
        /// </summary>
        public RequestOptions() { }
    }
}
