﻿using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Channels;

namespace Requests.Channel
{
    /// <summary>
    /// Record that transport the item and the prority of the item
    /// </summary>
    /// <param name="Priority">Priority of the item</param>
    /// <param name="Item">Item that the channel uses</param>
    public record PriorityItem<TElement>(int Priority, TElement Item);
    /// <summary>
    /// A implementation of channel with a priority listing
    /// </summary>
    /// <typeparam name="TElement"></typeparam>
    public class PriorityChannel<TElement> : Channel<PriorityItem<TElement>>
    {
        /// <summary>Task that indicates the channel has completed.</summary>
        private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        /// <summary>The items in the channel.</summary>
        private readonly ConcurrentQueue<PriorityItem<TElement>>[] _queues = null!;
        /// <summary>Readers blocked reading from the channel.</summary>
        private readonly Deque<AsyncOperation<PriorityItem<TElement>>> _blockedReaders = new();

        /// <summary>Readers waiting for a notification that data is available.</summary>
        private AsyncOperation<bool>? _waitingReadersTail;
        /// <summary>Set to non-null once Complete has been called.</summary>
        private Exception? _doneWriting;

        // The number of queues we store internally.
        private readonly int _priorityCount = 0;
        private int m_count = 0;

        internal ParallelChannelOptions Options { get; } = new();

        /// <summary>
        /// Initialize the priority channel.
        /// </summary>
        /// <param name="priCount">How many prioritys the channel sould handle</param>
        internal PriorityChannel(int priCount)
        {
            _priorityCount = priCount;
            _queues = new ConcurrentQueue<PriorityItem<TElement>>[_priorityCount];
            for (int i = 0; i < _priorityCount; i++)
                _queues[i] = new ConcurrentQueue<PriorityItem<TElement>>();

            Reader = new PriorityChannelReader(this);
            Writer = new PriorityChannelWriter(this);

        }

        /// <summary>
        /// Executes a parallel for-each operation on this instance of <see cref="PriorityChannel{TElement}"/>,
        /// enforcing a dynamic maximum degree of parallelism.
        /// </summary>
        /// <param name="body">Excecution function of parallel reader</param>
        /// <returns>A Task</returns>
        /// <exception cref="ArgumentNullException"></exception>
        internal Task RunParallelReader(Func<PriorityItem<TElement>, CancellationToken, ValueTask> body)
        {
            _ = body ?? throw new ArgumentNullException(nameof(body));

            SemaphoreSlim throttler = new(Options.MaxDegreeOfParallelism);
            Options.DegreeOfParallelismChangedDelta += (sender, delta) => OnParallismChanged(delta, throttler);

            return ParrarelRun(body, throttler);
        }

        private Task ParrarelRun(Func<PriorityItem<TElement>, CancellationToken, ValueTask> body, SemaphoreSlim throttler)
        {
            return Parallel.ForEachAsync(GetThrottledSource(throttler), Options, async (item, ct) =>
            {
                try { await body(item, ct).ConfigureAwait(false); }
                finally { throttler.Release(); }
            }).ContinueWith(task =>
            {
                Options.DegreeOfParallelismChangedDelta -= (object? sender, int delta) => OnParallismChanged(delta, throttler);
                return task;
            }, default, TaskContinuationOptions.DenyChildAttach, TaskScheduler.Default)
                .Unwrap();
        }

        private static void OnParallismChanged(int delta, SemaphoreSlim throttler)
        {
            if (delta > 0)
                throttler.Release(delta);
            else
                for (int i = delta; i < 0; i++)
                    throttler.WaitAsync();
        }

        private async IAsyncEnumerable<PriorityItem<TElement>> GetThrottledSource(SemaphoreSlim throttler)
        {
            await foreach (PriorityItem<TElement> element in Reader.ReadAllAsync().WithCancellation(default).ConfigureAwait(false))
            {
                if (Options?.EasyEndToken.IsPaused == true)
                {
                    _ = Writer.WriteAsync(element).AsTask();
                    break;
                }
                await throttler.WaitAsync().ConfigureAwait(false);
                yield return element;
            }
        }



        private sealed class PriorityChannelReader : ChannelReader<PriorityItem<TElement>>
        {
            internal readonly PriorityChannel<TElement> _parent;
            private readonly AsyncOperation<PriorityItem<TElement>> _readerSingleton;
            private readonly AsyncOperation<bool> _waiterSingleton;

            internal PriorityChannelReader(PriorityChannel<TElement> parent)
            {
                _parent = parent;
                _readerSingleton = new AsyncOperation<PriorityItem<TElement>>(true, pooled: true);
                _waiterSingleton = new AsyncOperation<bool>(true, pooled: true);
            }

            public override Task Completion => _parent._completion.Task;

            public override bool CanCount => true;

            public override bool CanPeek => true;

            public override int Count => _parent.m_count;

            public override ValueTask<PriorityItem<TElement>> ReadAsync(CancellationToken cancellationToken)
            {
                if (cancellationToken.IsCancellationRequested)
                    return new ValueTask<PriorityItem<TElement>>(Task.FromCanceled<PriorityItem<TElement>>(cancellationToken));

                PriorityChannel<TElement> parent = _parent;
                for (int i = 0; i < parent._priorityCount; i++)
                    if (parent._queues[i].TryDequeue(out PriorityItem<TElement>? item))
                    {
                        Interlocked.Decrement(ref parent.m_count);
                        CompleteIfDone(parent);
                        return new ValueTask<PriorityItem<TElement>>(item);
                    }

                return LockReadAsync(parent, cancellationToken);
            }

            private ValueTask<PriorityItem<TElement>> LockReadAsync(PriorityChannel<TElement> parent, CancellationToken cancellationToken)
            {
                lock (parent.SyncObj)
                {
                    for (int i = 0; i < parent._priorityCount; i++)
                        if (parent._queues[i].TryDequeue(out PriorityItem<TElement>? item))
                        {
                            Interlocked.Decrement(ref parent.m_count);
                            CompleteIfDone(parent);
                            return new ValueTask<PriorityItem<TElement>>(item);
                        }

                    if (parent._doneWriting != null)
                        return ChannelUtilities.GetInvalidCompletionValueTask<PriorityItem<TElement>>(parent._doneWriting);

                    if (!cancellationToken.CanBeCanceled)
                    {
                        AsyncOperation<PriorityItem<TElement>> singleton = _readerSingleton;
                        if (singleton.TryOwnAndReset())
                        {
                            parent._blockedReaders.EnqueueTail(singleton);
                            return singleton.ValueTaskOfT;
                        }
                    }

                    AsyncOperation<PriorityItem<TElement>> reader = new(true, false, cancellationToken);
                    parent._blockedReaders.EnqueueTail(reader);
                    return reader.ValueTaskOfT;
                }
            }

            /// <summary>
            /// Trys to read the next <see cref="PriorityItem{TElement}"/>
            /// </summary>
            /// <param name="item">nest <see cref="PriorityItem{TElement}"/> in list</param>
            /// <returns>A bool that indicates success</returns>
            public override bool TryRead([MaybeNullWhen(false)] out PriorityItem<TElement> item)
            {
                PriorityChannel<TElement> parent = _parent;

                for (int i = 0; i < parent._priorityCount; i++)
                    if (parent._queues[i].TryDequeue(out item))
                    {
                        Interlocked.Decrement(ref parent.m_count);
                        CompleteIfDone(parent);
                        return true;
                    }

                item = default;
                return false;
            }

            public override bool TryPeek([MaybeNullWhen(false)] out PriorityItem<TElement> item)
            {
                PriorityChannel<TElement> parent = _parent;
                for (int i = 0; i < _parent._priorityCount; i++)
                    if (parent._queues[i].TryPeek(out item))
                        return true;
                item = null;
                return false;
            }

            private static void CompleteIfDone(PriorityChannel<TElement> parent)
            {
                if (parent._doneWriting != null && parent._queues.All(x => x.IsEmpty))
                    ChannelUtilities.Complete(parent._completion, parent._doneWriting);

            }

            public override ValueTask<bool> WaitToReadAsync(CancellationToken cancellationToken)
            {
                if (cancellationToken.IsCancellationRequested)
                    return new ValueTask<bool>(Task.FromCanceled<bool>(cancellationToken));


                if (_parent._queues.Any(x => !x.IsEmpty))
                    return new ValueTask<bool>(true);


                PriorityChannel<TElement> parent = _parent;

                lock (parent.SyncObj)
                {
                    if (_parent._queues.Any(x => !x.IsEmpty))
                        return new ValueTask<bool>(true);

                    if (parent._doneWriting != null)
                        return parent._doneWriting != ChannelUtilities.s_doneWritingSentinel ?
                            new ValueTask<bool>(Task.FromException<bool>(parent._doneWriting)) :
                            default;

                    if (!cancellationToken.CanBeCanceled)
                    {
                        AsyncOperation<bool> singleton = _waiterSingleton;
                        if (singleton.TryOwnAndReset())
                        {
                            ChannelUtilities.QueueWaiter(ref parent._waitingReadersTail, singleton);
                            return singleton.ValueTaskOfT;
                        }
                    }

                    AsyncOperation<bool> waiter = new(true, false, cancellationToken);
                    ChannelUtilities.QueueWaiter(ref parent._waitingReadersTail, waiter);
                    return waiter.ValueTaskOfT;
                }
            }

        }

        private sealed class PriorityChannelWriter : ChannelWriter<PriorityItem<TElement>>
        {
            internal readonly PriorityChannel<TElement> _parent;
            internal PriorityChannelWriter(PriorityChannel<TElement> parent) => _parent = parent;

            public override bool TryComplete(Exception? error)
            {
                PriorityChannel<TElement> parent = _parent;
                bool completeTask;

                lock (parent.SyncObj)
                {
                    if (parent._doneWriting != null)
                        return false;

                    parent._doneWriting = error ?? ChannelUtilities.s_doneWritingSentinel;
                    completeTask = parent._queues.All(x => x.IsEmpty);
                }
                CompleatChannelUtils(error, parent, completeTask);

                return true;
            }

            private static void CompleatChannelUtils(Exception? error, PriorityChannel<TElement> parent, bool completeTask)
            {
                if (completeTask)
                    ChannelUtilities.Complete(parent._completion, error);

                ChannelUtilities.FailOperations<AsyncOperation<PriorityItem<TElement>>, PriorityItem<TElement>>(parent._blockedReaders, ChannelUtilities.CreateInvalidCompletionException(error));
                ChannelUtilities.WakeUpWaiters(ref parent._waitingReadersTail, result: false, error: error);
            }

            public override bool TryWrite(PriorityItem<TElement> pair)
            {
                PriorityChannel<TElement> parent = _parent;
                while (true)
                {
                    if (PriorityChannel<TElement>.PriorityChannelWriter.TryWriteLock(parent, pair, out AsyncOperation<PriorityItem<TElement>>? blockedReader, out AsyncOperation<bool>? waitingReadersTail) is bool result)
                        return result;

                    if (blockedReader != null)
                    {
                        if (blockedReader.TrySetResult(pair))
                        {
                            Interlocked.Increment(ref parent.m_count);
                            return true;
                        }
                    }
                    else
                    {
                        ChannelUtilities.WakeUpWaiters(ref waitingReadersTail, result: true);
                        return true;
                    }
                }
            }

            private static bool? TryWriteLock(PriorityChannel<TElement> parent, PriorityItem<TElement> pair, out AsyncOperation<PriorityItem<TElement>>? blockedReader, out AsyncOperation<bool>? waitingReadersTail)
            {
                waitingReadersTail = null;
                blockedReader = null;
                lock (parent.SyncObj)
                {

                    if (parent._doneWriting != null)
                        return false;

                    if (parent._blockedReaders.IsEmpty)
                    {
                        parent._queues[pair.Priority].Enqueue(pair);
                        Interlocked.Increment(ref parent.m_count);
                        waitingReadersTail = parent._waitingReadersTail;
                        if (waitingReadersTail == null)
                            return true;

                        parent._waitingReadersTail = null;
                    }
                    else
                        blockedReader = parent._blockedReaders.DequeueHead();
                }
                return null;
            }


            public override ValueTask<bool> WaitToWriteAsync(CancellationToken cancellationToken)
            {
                Exception? doneWriting = _parent._doneWriting;
                return
                    cancellationToken.IsCancellationRequested ? new ValueTask<bool>(Task.FromCanceled<bool>(cancellationToken)) :
                    doneWriting == null ? new ValueTask<bool>(true) : // unbounded writing can always be done if we haven't completed
                    doneWriting != ChannelUtilities.s_doneWritingSentinel ? new ValueTask<bool>(Task.FromException<bool>(doneWriting)) :
                    default;
            }

            public override ValueTask WriteAsync(PriorityItem<TElement> item, CancellationToken cancellationToken) =>
                cancellationToken.IsCancellationRequested ? new ValueTask(Task.FromCanceled(cancellationToken)) :
                TryWrite(item) ? default :
                new ValueTask(Task.FromException(ChannelUtilities.CreateInvalidCompletionException(_parent._doneWriting)));
        }

        /// <summary>
        /// Creates a Array out the actual members of this Channel
        /// </summary>
        /// <returns>A Array T</returns>
        public PriorityItem<TElement>[] ToArray()
        {
            PriorityItem<TElement>[] result;

            lock (_queues)
            {
                result = new PriorityItem<TElement>[Reader.Count];
                int index = 0;
                foreach (ConcurrentQueue<PriorityItem<TElement>> q in _queues)
                    if (q.Count > 0)
                    {
                        q.CopyTo(result, index);
                        index += q.Count;
                    }
                return result;
            }
        }



        /// <summary>Gets the object used to synchronize access to all state on this instance.</summary>
        private object SyncObj => _queues;
    }
}
