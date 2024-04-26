using System.Threading.Channels;

namespace Requests.Channel
{
    /// <summary>
    /// Provides helper methods for implementing channels.
    /// </summary>
    internal static class ChannelUtilities
    {
        /// <summary>
        /// A sentinel object used to indicate the completion of writing.
        /// </summary>
        internal static readonly Exception s_doneWritingSentinel = new(nameof(s_doneWritingSentinel));

        /// <summary>
        /// Completes the specified TaskCompletionSource.
        /// </summary>
        /// <param name="tcs">The TaskCompletionSource to complete.</param>
        /// <param name="error">
        /// The optional exception to complete the TaskCompletionSource with.
        /// If this is null or the DoneWritingSentinel, the TaskCompletionSource will be completed successfully.
        /// If this is an OperationCanceledException, it'll be completed with the exception's token.
        /// Otherwise, it'll be completed as faulted with the exception.
        /// </param>
        internal static void Complete(TaskCompletionSource tcs, Exception? error = null)
        {
            if (error is OperationCanceledException oce)
                tcs.TrySetCanceled(oce.CancellationToken);
            else if (error != null && error != s_doneWritingSentinel)
                tcs.TrySetException(error);
            else
                tcs.TrySetResult();
        }

        /// <summary>
        /// Gets a ValueTask that represents an error.
        /// </summary>
        /// <typeparam name="T">Specifies the type of the value that would have been returned.</typeparam>
        /// <param name="error">The error. This may be <see cref="s_doneWritingSentinel"/>.</param>
        /// <returns>The failed ValueTask.</returns>
        internal static ValueTask<T> GetInvalidCompletionValueTask<T>(Exception error)
        {
            if (error == s_doneWritingSentinel)
                return new(Task.FromException<T>(CreateInvalidCompletionException()));

            else
                return new(error is OperationCanceledException oce ? Task.FromCanceled<T>(oce.CancellationToken.IsCancellationRequested ? oce.CancellationToken : new CancellationToken(true)) : Task.FromException<T>(CreateInvalidCompletionException(error)));

        }

        /// <summary>
        /// Queues a waiter if it is null.
        /// </summary>
        /// <param name="tail">The tail of the queue.</param>
        /// <param name="waiter">The waiter to queue.</param>
        internal static void QueueWaiter(ref AsyncOperation<bool>? tail, AsyncOperation<bool> waiter)
        {
            AsyncOperation<bool>? c = tail;
            if (c == null)
                waiter.Next = waiter;
            else
            {
                waiter.Next = c.Next;
                c.Next = waiter;
            }
            tail = waiter;
        }

        /// <summary>
        /// Iterates through all AsyncOperation waiters and sets the result.
        /// </summary>
        /// <param name="listTail">The tail of the list.</param>
        /// <param name="result">The result to set.</param>
        /// <param name="error">The error to set, if any.</param>
        internal static void WakeUpWaiters(ref AsyncOperation<bool>? listTail, bool result, Exception? error = null)
        {
            AsyncOperation<bool>? tail = listTail;
            if (tail != null)
            {
                listTail = null;
                AsyncOperation<bool> head = tail.Next!;
                AsyncOperation<bool> c = head;
                do
                {
                    AsyncOperation<bool> next = c.Next!;
                    c.Next = null;
                    _ = error != null ? c.TrySetException(error) : c.TrySetResult(result);
                    c = next;
                }
                while (c != head);
            }
        }

        /// <summary>
        /// Removes all operations from the queue and fails each one.
        /// </summary>
        /// <param name="operations">The queue of operations to be completed.</param>
        /// <param name="error">The error to complete each operation with.</param>
        internal static void FailOperations<T, TInner>(Deque<T> operations, Exception error) where T : AsyncOperation<TInner>
        {
            while (!operations.IsEmpty)
                operations.DequeueHead().TrySetException(error);
        }

        /// <summary>
        /// Creates and returns an exception to indicate that a channel has been closed.
        /// </summary>
        /// <param name="inner">Optional inner exception. If it's an OperationCanceledException, it's returned as is. If it's not null and not DoneWritingSentinel, a new ChannelClosedException is created with it. Otherwise, a new ChannelClosedException is created without an inner exception.</param>
        internal static Exception CreateInvalidCompletionException(Exception? inner = null) =>
            inner is OperationCanceledException ? inner :
            inner != null && inner != s_doneWritingSentinel ? new ChannelClosedException(inner) :
            new ChannelClosedException();
    }
}
