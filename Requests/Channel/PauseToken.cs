namespace Requests.Channel
{
    /// <summary>
    /// Token to indicate if process is on hold
    /// </summary>
    public readonly struct PauseToken
    {
        private readonly PauseTokenSource tokenSource;

        /// <summary>
        /// If process is on hold
        /// </summary>
        public bool IsPaused => tokenSource?.IsPaused == true;

        internal PauseToken(PauseTokenSource source) => tokenSource = source;

        /// <summary>
        /// Get Task to await while the token is paused
        /// </summary>
        /// <returns>Task to await</returns>
        public Task WaitWhilePausedAsync() => IsPaused ? tokenSource.WaitWhilePausedAsync() : PauseTokenSource.CompletedTask;

    }
}