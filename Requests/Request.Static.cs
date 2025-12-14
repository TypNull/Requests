
using System.Runtime.CompilerServices;

namespace Requests
{
    /// <summary>
    /// Static helper for Request yield operations.
    /// Use 'await Request.Yield();' anywhere in request logic to yield control back to the scheduler.
    /// </summary>
    public static class Request
    {
        private static readonly AsyncLocal<IRequest?> _current = new();

        /// <summary>
        /// Gets the current request context.
        /// </summary>
        internal static IRequest? Current => _current.Value;

        /// <summary>
        /// Sets the current request context.
        /// </summary>
        internal static void SetCurrent(IRequest? request) => _current.Value = request;

        /// <summary>
        /// Yields control back to the request scheduler.
        /// Use: await Request.Yield();
        /// </summary>
        /// <remarks>
        /// If called outside a request context, this completes synchronously as a no-op.
        /// If called within a request context, yields through the request's YieldAsync() mechanism.
        /// </remarks>
        public static YieldAwaitable Yield() => new(_current.Value);
    }

    /// <summary>
    /// Provides an awaitable type that enables yielding in request contexts.
    /// </summary>
    public readonly struct YieldAwaitable : ICriticalNotifyCompletion
    {
        private readonly IRequest? _context;

        internal YieldAwaitable(IRequest? context) => _context = context;

        /// <summary>
        /// Gets an awaiter for this awaitable (returns itself).
        /// </summary>
        public YieldAwaitable GetAwaiter() => this;

        /// <summary>
        /// Returns true if there's no context (completes synchronously as no-op).
        /// Returns false if there's a context (needs to yield through the request).
        /// </summary>
        public bool IsCompleted => _context == null;

        /// <summary>
        /// Called when the await operation completes. No result is returned.
        /// </summary>
        public void GetResult() { }

        /// <summary>
        /// Schedules the continuation to run after the yield operation completes.
        /// Captures ExecutionContext.
        /// </summary>
        public void OnCompleted(Action continuation)
        {
            ArgumentNullException.ThrowIfNull(continuation);
            _ = ScheduleContinuationAsync(_context!, continuation);
        }

        /// <summary>
        /// Schedules the continuation to run after the yield operation completes.
        /// Does not capture ExecutionContext.
        /// </summary>
        public void UnsafeOnCompleted(Action continuation)
        {
            ArgumentNullException.ThrowIfNull(continuation);
            _ = ScheduleContinuationAsync(_context!, continuation);
        }

        /// <summary>
        /// Schedules the continuation after yielding through the request context.
        /// </summary>
        private static async Task ScheduleContinuationAsync(IRequest context, Action continuation)
        {
            try
            {
                await context.YieldAsync().ConfigureAwait(false);
                continuation();
            }
            catch
            {
                // Still invoke continuation to prevent deadlock
                continuation();
            }
        }
    }
}
