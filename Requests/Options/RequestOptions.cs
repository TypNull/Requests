namespace Requests.Options
{
    /// <summary>
    /// A record that implements the <see cref="IRequestOptions{TCompleated, TFailed}"/> interface.
    /// </summary>
    /// <typeparam name="TCompleated">The return type if the request is completed successfully.</typeparam>
    /// <typeparam name="TFailed">The return type if the request fails.</typeparam>
    public record RequestOptions<TCompleated, TFailed> : IRequestOptions<TCompleated, TFailed>
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
        public Notify<IRequest>? RequestStarted { get; set; }

        ///<inheritdoc />
        public Notify<IRequest, TCompleated>? RequestCompleated { get; set; }

        ///<inheritdoc />
        public Notify<IRequest, TFailed>? RequestFailed { get; set; }

        ///<inheritdoc />
        public Notify<IRequest>? RequestCancelled { get; set; }

        /// <summary>
        /// Copy constructor for the RequestOptions record.
        /// </summary>
        /// <param name="options">The RequestOptions instance to copy from.</param>
        protected RequestOptions(RequestOptions<TCompleated, TFailed> options)
        {
            Priority = options.Priority;
            Handler = options.Handler;
            NumberOfAttempts = options.NumberOfAttempts;
            CancellationToken = options.CancellationToken;
            AutoStart = options.AutoStart;
            DelayBetweenAttemps = options.DelayBetweenAttemps;
            DeployDelay = options.DeployDelay;
            RequestCancelled += options.RequestCancelled;
            RequestStarted += options.RequestStarted;
            RequestFailed += options.RequestFailed;
            RequestCompleated += options.RequestCompleated;
        }

        /// <summary>
        /// Default constructor for the RequestOptions record.
        /// </summary>
        public RequestOptions() { }
    }
}
