namespace Requests.Options
{/// <summary>
 /// Indicates the state of a <see cref="RequestObject"/>.
 /// </summary>
    public enum RequestState
    {
        /// <summary>
        /// <see cref="RequestObject"/> can be started.
        /// </summary>
        Available,
        /// <summary>
        /// <see cref="RequestObject"/> is running.
        /// </summary>
        Running,
        /// <summary>
        /// <see cref="RequestObject"/> is sucessfuly compleated.
        /// </summary>
        Compleated,
        /// <summary>
        /// <see cref="RequestObject"/> is paused.
        /// </summary>
        Onhold,
        /// <summary>
        /// <see cref="RequestObject"/> is cancelled.
        /// </summary>
        Waiting,
        /// <summary>
        /// <see cref="RequestObject"/> is cancelled.
        /// </summary>
        Cancelled,
        /// <summary>
        /// <see cref="RequestObject"/> failed.
        /// </summary>
        Failed
    }
}
