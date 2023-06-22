namespace Requests.Options
{
    /// <summary>
    /// Record implementation of IRequestOptions
    /// 
    /// </summary>
    /// <typeparam name="TCompleated">Type of return if compleated</typeparam>
    /// <typeparam name="TFailed">Type of return if failed</typeparam>
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
        public NotifyVoid? RequestStarted { get; set; }

        ///<inheritdoc />
        public Notify<TCompleated>? RequestCompleated { get; set; }

        ///<inheritdoc />
        public Notify<TFailed>? RequestFailed { get; set; }

        ///<inheritdoc />
        public NotifyVoid? RequestCancelled { get; set; }

        /// <summary>
        /// Copy constructor for the record <see cref="RequestOptions{TCompleated, TFailed}"/>
        /// </summary>
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
        /// Main constructor for the record <see cref="RequestOptions{TCompleated, TFailed}"/>
        /// </summary>
        public RequestOptions() { }
    }
}
