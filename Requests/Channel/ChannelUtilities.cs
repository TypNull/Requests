using System.Threading.Channels;

namespace Requests.Channel
{
    /// <summary>Provides internal helper methods for implementing channels.</summary>
    internal static class ChannelUtilities
    {
        /// <summary>Sentinel object used to indicate being done writing.</summary>
        internal static readonly Exception s_doneWritingSentinel = new(nameof(s_doneWritingSentinel));

        /// <summary>Completes the specified TaskCompletionSource.</summary>
        /// <param name="tcs">The source to complete.</param>
        /// <param name="error">
        /// The optional exception with which to complete.
        /// If this is null or the DoneWritingSentinel, the source will be completed successfully.
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

        /// <summary>Gets a value task representing an error.</summary>
        /// <typeparam name="T">Specifies the type of the value that would have been returned.</typeparam>
        /// <param name="error">The error.  This may be <see cref="s_doneWritingSentinel"/>.</param>
        /// <returns>The failed task.</returns>
        internal static ValueTask<T> GetInvalidCompletionValueTask<T>(Exception error)
        {
            if (error == s_doneWritingSentinel)
                return new(Task.FromException<T>(CreateInvalidCompletionException()));

            else
                return new(error is OperationCanceledException oce ? Task.FromCanceled<T>(oce.CancellationToken.IsCancellationRequested ? oce.CancellationToken : new CancellationToken(true)) : Task.FromException<T>(CreateInvalidCompletionException(error)));

        }
        /// <summary>
        /// Sets the Queue waite if it is null
        /// </summary>
        /// <param name="tail"></param>
        /// <param name="waiter"></param>
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
        /// Goes throu all asyncOperation waiters and sets the result
        /// </summary>
        /// <param name="listTail"></param>
        /// <param name="result"></param>
        /// <param name="error"></param>
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

        /// <summary>Removes all operations from the queue, failing each.</summary>
        /// <param name="operations">The queue of operations to complete.</param>
        /// <param name="error">The error with which to complete each operations.</param>
        internal static void FailOperations<T, TInner>(Deque<T> operations, Exception error) where T : AsyncOperation<TInner>
        {
            while (!operations.IsEmpty)
                operations.DequeueHead().TrySetException(error);
        }

        /// <summary>Creates and returns an exception object to indicate that a channel has been closed.</summary>
        internal static Exception CreateInvalidCompletionException(Exception? inner = null) =>
            inner is OperationCanceledException ? inner :
            inner != null && inner != s_doneWritingSentinel ? new ChannelClosedException(inner) :
            new ChannelClosedException();
    }
}
