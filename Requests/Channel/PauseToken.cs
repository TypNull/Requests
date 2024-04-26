namespace Requests.Channel
{
    /// <summary>
    /// Represents a token that indicates whether a process is paused or not.
    /// </summary>
    public readonly struct PauseToken
    {
        private readonly PauseTokenSource tokenSource;

        /// <summary>
        /// Gets a value indicating whether the process is paused.
        /// </summary>
        /// <value>
        ///   <c>true</c> if this instance is paused; otherwise, <c>false</c>.
        /// </value>
        public bool IsPaused => tokenSource?.IsPaused == true;

        /// <summary>
        /// Initializes a new instance of the <see cref="PauseToken"/> struct.
        /// </summary>
        /// <param name="source">The source of the pause token.</param>
        internal PauseToken(PauseTokenSource source) => tokenSource = source;

        /// <summary>
        /// Asynchronously waits while the token is paused.
        /// </summary>
        /// <returns>
        /// A <see cref="Task"/> that completes when the token is no longer paused.
        /// </returns>
        public Task WaitWhilePausedAsync() => IsPaused ? tokenSource.WaitWhilePausedAsync() : PauseTokenSource.CompletedTask;

    }
}