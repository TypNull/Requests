namespace Requests.Channel
{
    /// <summary>
    /// A class to share a <see cref="PauseToken"/> that indicates if this source was paused.
    /// </summary>
    public class PauseTokenSource
    {
        private volatile TaskCompletionSource<bool>? tcsPaused;
        internal static readonly Task CompletedTask = Task.FromResult(true);
        private readonly PauseToken? _pt = null;

        /// <summary>
        /// <see cref="PauseToken"/> that indicates if this source was paused.
        /// </summary>
        public PauseToken Token => new(this);

        /// <summary>
        /// Bool thta indicates if this <see cref="PauseTokenSource"/> was paused.
        /// </summary>
        public bool IsPaused => tcsPaused != null || _pt?.IsPaused == true;

        /// <summary>
        /// Main Constructor that can link this <see cref="PauseTokenSource"/> to another <see cref="PauseTokenSource"/>.
        /// </summary>
        /// <param name="pt">PauseToken to link</param>
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
        /// Waits while this <see cref="PauseTokenSource"/> is paused.
        /// </summary>
        internal Task WaitWhilePausedAsync() => tcsPaused?.Task ?? CompletedTask;

    }
}
