namespace Requests.Options
{/// <summary>
 /// Indicates the state of a <see cref="IRequest"/>.
 /// </summary>
    public enum RequestState
    {
        /// <summary>
        /// <see cref="IRequest"/> can be started.
        /// </summary>
        Idle,
        /// <summary>
        /// <see cref="IRequest"/> is running.
        /// </summary>
        Running,
        /// <summary>
        /// <see cref="IRequest"/> is sucessfuly compleated.
        /// </summary>
        Compleated,
        /// <summary>
        /// <see cref="IRequest"/> is paused.
        /// </summary>
        Paused,
        /// <summary>
        /// <see cref="IRequest"/> is cancelled.
        /// </summary>
        Waiting,
        /// <summary>
        /// <see cref="IRequest"/> is cancelled.
        /// </summary>
        Cancelled,
        /// <summary>
        /// <see cref="IRequest"/> failed.
        /// </summary>
        Failed
    }
}
