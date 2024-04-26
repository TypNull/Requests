namespace Requests.Channel
{
    /// <summary>
    /// A class that provides a shared <see cref="PauseToken"/> to indicate the pause state of a source.
    /// </summary>
    public class PauseTokenSource
    {
        private volatile TaskCompletionSource<bool>? tcsPaused;
        internal static readonly Task CompletedTask = Task.FromResult(true);
        private readonly PauseToken? _pt = null;

        /// <summary>
        /// Gets a <see cref="PauseToken"/> that reflects the pause state of this source.
        /// </summary>
        public PauseToken Token => new(this);

        /// <summary>
        /// Gets a value indicating whether this <see cref="PauseTokenSource"/> is paused.
        /// </summary>
        public bool IsPaused => tcsPaused != null || _pt?.IsPaused == true;

        /// <summary>
        /// Initializes a new instance of the <see cref="PauseTokenSource"/> class that can be linked to another <see cref="PauseTokenSource"/>.
        /// </summary>
        /// <param name="pt">The <see cref="PauseToken"/> to link.</param>
        public PauseTokenSource(PauseToken? pt = null) => _pt = pt;

        /// <summary>
        /// Pauses this <see cref="PauseTokenSource"/>.
        /// </summary>
        public void Pause() => Interlocked.CompareExchange(ref tcsPaused, new TaskCompletionSource<bool>(), null);

        /// <summary>
        /// Resumes this <see cref="PauseTokenSource"/>.
        /// </summary>
        public void Resume()
        {
            while (true)
            {
                TaskCompletionSource<bool>? tcs = tcsPaused;

                if (tcs == null)
                    return;

                if (Interlocked.CompareExchange(ref tcsPaused, null, tcs) == tcs)
                {
                    tcs.SetResult(true);
                    break;
                }
            }
        }

        /// <summary>
        /// Waits asynchronously while this <see cref="PauseTokenSource"/> is paused.
        /// </summary>
        internal Task WaitWhilePausedAsync() => tcsPaused?.Task ?? CompletedTask;

    }
}
