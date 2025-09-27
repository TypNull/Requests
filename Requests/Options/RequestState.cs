namespace Requests.Options
{
    /// <summary>
    /// Enum that represents the various states of an <see cref="IRequest"/>.
    /// </summary>
    public enum RequestState
    {
        /// <summary>
        /// State indicating that the <see cref="IRequest"/> is ready to be processed.
        /// </summary>
        Idle,
        /// <summary>
        /// State indicating that the <see cref="IRequest"/> is currently in progress.
        /// </summary>
        Running,
        /// <summary>
        /// State indicating that the <see cref="IRequest"/> has been completed successfully.
        /// </summary>
        Completed,
        /// <summary>
        /// State indicating that the <see cref="IRequest"/> is currently paused.
        /// </summary>
        Paused,
        /// <summary>
        /// State indicating that the <see cref="IRequest"/> is waiting to be deployed to the handler.
        /// </summary>
        Waiting,
        /// <summary>
        /// State indicating that the <see cref="IRequest"/> has been cancelled.
        /// </summary>
        Cancelled,
        /// <summary>
        /// State indicating that the <see cref="IRequest"/> has failed.
        /// </summary>
        Failed
    }
}
