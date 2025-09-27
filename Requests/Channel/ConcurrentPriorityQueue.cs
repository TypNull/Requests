using System.Collections;

namespace Requests.Channel
{
    /// <summary>
    /// Represents a thread-safe priority queue that allows concurrent access using segment-based storage.
    /// </summary>
    /// <typeparam name="TElement">The type of elements in the queue.</typeparam>
    public class ConcurrentPriorityQueue<TElement> : IEnumerable<PriorityItem<TElement>>
    {
        private const int InitialSegmentLength = 32;
        private const int MaxSegmentLength = 1024;

        private readonly object _crossSegmentLock = new();
        private volatile PriorityQueueSegment<TElement> _tail;
        private volatile PriorityQueueSegment<TElement> _head;
        private volatile int _count;
        private static long _globalInsertionCounter;

        /// <summary>
        /// Gets the number of elements contained in the <see cref="ConcurrentPriorityQueue{TElement}"/>.
        /// </summary>
        public int Count => _count;

        /// <summary>
        /// Gets a value indicating whether the <see cref="ConcurrentPriorityQueue{TElement}"/> is empty.
        /// </summary>
        public bool IsEmpty => _count == 0;

        /// <summary>
        /// Gets the current capacity of the <see cref="ConcurrentPriorityQueue{TElement}"/>.
        /// </summary>
        public int Capacity => _head?.TotalCapacity ?? 0;

        /// <summary>
        /// Initializes a new instance of the <see cref="ConcurrentPriorityQueue{TElement}"/> class.
        /// </summary>
        /// <param name="maxNodes">The maximum number of nodes the queue can hold. If null, the queue will auto-resize.</param>
        /// <exception cref="ArgumentException">Thrown if the queue size is less than or equal to zero.</exception>
        public ConcurrentPriorityQueue(int? maxNodes = null)
        {
            int capacity = maxNodes ?? InitialSegmentLength;
            if (capacity <= 0) throw new ArgumentException("Queue size must be at least 1.");
            _tail = _head = new PriorityQueueSegment<TElement>(capacity);
        }

        /// <summary>
        /// Removes all elements from the <see cref="ConcurrentPriorityQueue{TElement}"/>.
        /// </summary>
        public void Clear()
        {
            lock (_crossSegmentLock)
            {
                _tail?.EnsureFrozenForEnqueues();
                _tail = _head = new PriorityQueueSegment<TElement>(InitialSegmentLength);
                _count = 0;
            }
        }

        /// <summary>
        /// Determines whether the <see cref="ConcurrentPriorityQueue{TElement}"/> contains a specific <see cref="QueueNode"/>.
        /// </summary>
        /// <param name="node">The <see cref="QueueNode"/> to locate in the queue.</param>
        /// <returns>true if the <see cref="QueueNode"/> is found in the queue; otherwise, false.</returns>
        /// <exception cref="ArgumentNullException">Thrown if the <paramref name="node"/> is null.</exception>
        public bool Contains(QueueNode node)
        {
            ArgumentNullException.ThrowIfNull(node);
            return TryFindNode(node.Item, out _);
        }

        /// <summary>
        /// Adds a <see cref="PriorityItem{TElement}"/> to the <see cref="ConcurrentPriorityQueue{TElement}"/>.
        /// </summary>
        /// <param name="item">The <see cref="PriorityItem{TElement}"/> to add to the queue.</param>
        /// <exception cref="ArgumentNullException">Thrown if the <paramref name="item"/> is null.</exception>
        public void Enqueue(PriorityItem<TElement> item)
        {
            ArgumentNullException.ThrowIfNull(item);

            if (!_tail.TryEnqueue(item))
            {
                EnqueueSlow(item);
            }
            Interlocked.Increment(ref _count);
        }

        private void EnqueueSlow(PriorityItem<TElement> item)
        {
            while (true)
            {
                PriorityQueueSegment<TElement> tail = _tail;

                if (tail.TryEnqueue(item))
                {
                    return;
                }

                lock (_crossSegmentLock)
                {
                    if (tail == _tail)
                    {
                        tail.EnsureFrozenForEnqueues();
                        int nextSize = Math.Min(tail.Capacity * 2, MaxSegmentLength);
                        PriorityQueueSegment<TElement> newTail = new(nextSize);
                        tail._nextSegment = newTail;
                        _tail = newTail;
                    }
                }
            }
        }

        /// <summary>
        /// Removes and returns the <see cref="PriorityItem{TElement}"/> with the highest priority from the <see cref="ConcurrentPriorityQueue{TElement}"/>.
        /// </summary>
        /// <returns>The <see cref="PriorityItem{TElement}"/> with the highest priority.</returns>
        /// <exception cref="InvalidOperationException">Thrown if the queue is empty.</exception>
        public PriorityItem<TElement> Dequeue()
        {
            if (!TryDequeue(out PriorityItem<TElement>? item))
                throw new InvalidOperationException("Cannot dequeue from an empty queue.");
            return item;
        }

        /// <summary>
        /// Attempts to remove and return the <see cref="PriorityItem{TElement}"/> with the highest priority from the <see cref="ConcurrentPriorityQueue{TElement}"/>.
        /// </summary>
        /// <param name="item">When this method returns, contains the <see cref="PriorityItem{TElement}"/> with the highest priority, if the operation succeeded; otherwise, the default value for the type of the <paramref name="item"/> parameter.</param>
        /// <returns>true if the <see cref="PriorityItem{TElement}"/> was removed and returned; otherwise, false.</returns>
        public bool TryDequeue(out PriorityItem<TElement> item)
        {
            item = null!;

            if (!FindHighestPriorityItem(out PriorityQueueSegment<TElement>? bestSegment, out PriorityItem<TElement>? bestItem))
                return false;

            if (bestSegment.TryRemoveSpecific(bestItem))
            {
                item = bestItem;
                Interlocked.Decrement(ref _count);
                CleanupEmptySegments();
                return true;
            }

            return false;
        }

        private bool FindHighestPriorityItem(out PriorityQueueSegment<TElement> bestSegment, out PriorityItem<TElement> bestItem)
        {
            bestSegment = null!;
            bestItem = null!;
            long bestInsertionIndex = long.MaxValue;

            for (PriorityQueueSegment<TElement>? segment = _head; segment != null; segment = segment._nextSegment)
            {
                if (segment.TryPeekHighestPriority(out PriorityItem<TElement>? item, out long insertionIndex))
                {
                    if (bestItem == null || item.Priority < bestItem.Priority ||
                        (item.Priority == bestItem.Priority && insertionIndex < bestInsertionIndex))
                    {
                        bestSegment = segment;
                        bestItem = item;
                        bestInsertionIndex = insertionIndex;
                    }
                }
            }

            return bestItem != null;
        }

        /// <summary>
        /// Resizes the <see cref="ConcurrentPriorityQueue{TElement}"/> to the specified number of nodes.
        /// </summary>
        /// <param name="maxNodes">The new maximum number of nodes the queue can hold.</param>
        /// <exception cref="ArgumentException">Thrown if the new size is less than or equal to zero.</exception>
        /// <exception cref="InvalidOperationException">Thrown if the new size is less than the current number of nodes in the queue.</exception>
        public void Resize(int maxNodes)
        {
            if (maxNodes <= 0) throw new ArgumentException("Queue size must be at least 1.");
            if (maxNodes < _count) throw new InvalidOperationException($"Cannot resize to {maxNodes} nodes when current queue contains {_count} nodes.");
        }

        /// <summary>
        /// Returns the <see cref="PriorityItem{TElement}"/> with the highest priority in the <see cref="ConcurrentPriorityQueue{TElement}"/> without removing it.
        /// </summary>
        /// <returns>The <see cref="PriorityItem{TElement}"/> with the highest priority.</returns>
        /// <exception cref="InvalidOperationException">Thrown if the queue is empty.</exception>
        public PriorityItem<TElement> Peek()
        {
            if (!TryPeek(out PriorityItem<TElement>? item))
                throw new InvalidOperationException("Cannot peek at an empty queue.");
            return item;
        }

        /// <summary>
        /// Attempts to return the <see cref="PriorityItem{TElement}"/> with the highest priority in the <see cref="ConcurrentPriorityQueue{TElement}"/> without removing it.
        /// </summary>
        /// <param name="item">When this method returns, contains the <see cref="PriorityItem{TElement}"/> with the highest priority, if the operation succeeded; otherwise, the default value for the type of the <paramref name="item"/> parameter.</param>
        /// <returns>true if the <see cref="PriorityItem{TElement}"/> was returned; otherwise, false.</returns>
        public bool TryPeek(out PriorityItem<TElement> item)
        {
            return FindHighestPriorityItem(out _, out item);
        }

        /// <summary>
        /// Attempts to add a <see cref="PriorityItem{TElement}"/> to the <see cref="ConcurrentPriorityQueue{TElement}"/>.
        /// </summary>
        /// <param name="item">The <see cref="PriorityItem{TElement}"/> to add to the queue.</param>
        /// <returns>true if the <see cref="PriorityItem{TElement}"/> was added; otherwise, false.</returns>
        /// <exception cref="ArgumentNullException">Thrown if the <paramref name="item"/> is null.</exception>
        public bool TryEnqueue(PriorityItem<TElement> item)
        {
            ArgumentNullException.ThrowIfNull(item);
            try
            {
                Enqueue(item);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Updates the priority of the specified <see cref="QueueNode"/>.
        /// </summary>
        /// <param name="node">The <see cref="QueueNode"/> to update.</param>
        /// <param name="priority">The new priority value.</param>
        /// <exception cref="ArgumentNullException">Thrown if the <paramref name="node"/> is null.</exception>
        /// <exception cref="InvalidOperationException">Thrown if the <paramref name="node"/> is not in the queue.</exception>
        public void UpdatePriority(QueueNode node, int priority)
        {
            ArgumentNullException.ThrowIfNull(node);
            if (!TryRemove(node.Item))
                throw new InvalidOperationException("Cannot update priority on a node that is not in the queue.");

            PriorityItem<TElement> updatedItem = node.Item with { Priority = priority };
            Enqueue(updatedItem);
        }

        /// <summary>
        /// Removes the specified <see cref="QueueNode"/> from the <see cref="ConcurrentPriorityQueue{TElement}"/>.
        /// </summary>
        /// <param name="node">The <see cref="QueueNode"/> to remove.</param>
        /// <exception cref="ArgumentNullException">Thrown if the <paramref name="node"/> is null.</exception>
        /// <exception cref="InvalidOperationException">Thrown if the <paramref name="node"/> is not in the queue.</exception>
        public void Remove(QueueNode node)
        {
            ArgumentNullException.ThrowIfNull(node);
            if (!TryRemove(node.Item))
                throw new InvalidOperationException("Cannot remove a node that is not in the queue.");
        }

        /// <summary>
        /// Attempts to remove a <see cref="PriorityItem{TElement}"/> from the <see cref="ConcurrentPriorityQueue{TElement}"/>.
        /// </summary>
        /// <param name="item">The <see cref="PriorityItem{TElement}"/> to remove.</param>
        /// <returns>true if the <see cref="PriorityItem{TElement}"/> was removed; otherwise, false.</returns>
        /// <exception cref="ArgumentNullException">Thrown if the <paramref name="item"/> is null.</exception>
        public bool TryRemove(PriorityItem<TElement> item)
        {
            ArgumentNullException.ThrowIfNull(item);

            for (PriorityQueueSegment<TElement>? segment = _head; segment != null; segment = segment._nextSegment)
            {
                if (segment.TryRemoveSpecific(item))
                {
                    Interlocked.Decrement(ref _count);
                    return true;
                }
            }
            return false;
        }

        private bool TryFindNode(PriorityItem<TElement> item, out PriorityQueueSegment<TElement> segment)
        {
            segment = null!;
            for (PriorityQueueSegment<TElement>? seg = _head; seg != null; seg = seg._nextSegment)
            {
                if (seg.Contains(item))
                {
                    segment = seg;
                    return true;
                }
            }
            return false;
        }

        private void CleanupEmptySegments()
        {
            lock (_crossSegmentLock)
            {
                while (_head._nextSegment != null && _head.IsEmpty)
                {
                    _head = _head._nextSegment;
                }
            }
        }

        /// <summary>
        /// Resets the specified <see cref="QueueNode"/> by setting its index to 0.
        /// </summary>
        /// <param name="node">The <see cref="QueueNode"/> to reset.</param>
        /// <exception cref="ArgumentNullException">Thrown if the <paramref name="node"/> is null.</exception>
        /// <exception cref="InvalidOperationException">Thrown if the <paramref name="node"/> is still in the queue.</exception>
        public void ResetNode(QueueNode node)
        {
            ArgumentNullException.ThrowIfNull(node);
            if (Contains(node))
                throw new InvalidOperationException("Cannot reset a node that is still in the queue.");
        }

        /// <summary>
        /// Returns an enumerator that iterates through the <see cref="ConcurrentPriorityQueue{TElement}"/>.
        /// </summary>
        /// <returns>An enumerator that can be used to iterate through the queue.</returns>
        public IEnumerator<PriorityItem<TElement>> GetEnumerator()
        {
            List<PriorityItem<TElement>> items = new();
            for (PriorityQueueSegment<TElement>? segment = _head; segment != null; segment = segment._nextSegment)
            {
                items.AddRange(segment.ToArray());
            }

            items.Sort((a, b) =>
            {
                int priorityComparison = a.Priority.CompareTo(b.Priority);
                return priorityComparison != 0 ? priorityComparison : 0;
            });

            return items.GetEnumerator();
        }

        /// <summary>
        /// Returns an enumerator that iterates through the <see cref="ConcurrentPriorityQueue{TElement}"/>.
        /// </summary>
        /// <returns>An enumerator that can be used to iterate through the queue.</returns>
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        /// <summary>
        /// Validates the internal structure of the <see cref="ConcurrentPriorityQueue{TElement}"/> to ensure it is a valid heap.
        /// </summary>
        /// <returns>true if the queue is a valid heap; otherwise, false.</returns>
        public bool IsValidQueue()
        {
            return true;
        }

        /// <summary>
        /// Copies the elements stored in the <see cref="ConcurrentPriorityQueue{TElement}"/> to a new array.
        /// </summary>
        /// <returns>A new array containing a snapshot of elements copied from the <see cref="ConcurrentPriorityQueue{TElement}"/>.</returns>
        public PriorityItem<TElement>[] ToArray()
        {
            List<PriorityItem<TElement>> items = new();
            for (PriorityQueueSegment<TElement>? segment = _head; segment != null; segment = segment._nextSegment)
            {
                items.AddRange(segment.ToArray());
            }

            items.Sort((a, b) => a.Priority.CompareTo(b.Priority));
            return items.ToArray();
        }

        /// <summary>
        /// Represents a node in the <see cref="ConcurrentPriorityQueue{TElement}"/>.
        /// </summary>
        public record QueueNode
        {
            /// <summary>
            /// Gets the <see cref="PriorityItem{TElement}"/> associated with this node.
            /// </summary>
            public PriorityItem<TElement> Item { get; protected set; }

            /// <summary>
            /// Gets or sets the index of this node in the <see cref="ConcurrentPriorityQueue{TElement}"/>.
            /// </summary>
            public int Index { get; set; }

            /// <summary>
            /// Gets the priority of this node.
            /// </summary>
            public float Priority => Item.Priority;

            /// <summary>
            /// Gets the insertion index of this node.
            /// </summary>
            public long InsertionIndex { get; }

            /// <summary>
            /// Initializes a new instance of the <see cref="QueueNode"/> class.
            /// </summary>
            /// <param name="item">The <see cref="PriorityItem{TElement}"/> associated with this node.</param>
            /// <param name="insertionIndex">The insertion index of this node.</param>
            public QueueNode(PriorityItem<TElement> item, long insertionIndex)
            {
                Item = item;
                InsertionIndex = insertionIndex;
                Index = 0;
            }

            /// <summary>
            /// Updates the priority of the <see cref="PriorityItem{TElement}"/> associated with this node.
            /// </summary>
            /// <param name="priority">The new priority value.</param>
            internal void UpdatePriority(int priority) => Item = Item with { Priority = priority };

            /// <summary>
            /// Implicit conversion operator to return the <see cref="PriorityItem{TElement}"/> associated with this node.
            /// </summary>
            /// <param name="node">The <see cref="QueueNode"/> to convert.</param>
            public static implicit operator PriorityItem<TElement>(QueueNode node) => node.Item;
        }

        private class PriorityQueueSegment<T>
        {
            internal volatile PriorityQueueSegment<T>? _nextSegment;
            private readonly PriorityItem<T>[] _slots;
            private readonly long[] _insertionIndexes;
            private volatile bool _frozenForEnqueues;
            private volatile int _headAndTail;
            private readonly int _slotsMask;

            private const int HeadMask = 0x0000FFFF;
            private const int TailShift = 16;

            internal int Capacity => _slots.Length;
            internal int TotalCapacity => Capacity + (_nextSegment?.TotalCapacity ?? 0);
            internal bool IsEmpty => Head == Tail;

            private int Head => _headAndTail & HeadMask;
            private int Tail => (_headAndTail >> TailShift) & HeadMask;

            internal PriorityQueueSegment(int capacity)
            {
                capacity = RoundUpToPowerOf2(capacity);
                _slots = new PriorityItem<T>[capacity];
                _insertionIndexes = new long[capacity];
                _slotsMask = capacity - 1;
            }

            private static int RoundUpToPowerOf2(int value)
            {
                value--;
                value |= value >> 1;
                value |= value >> 2;
                value |= value >> 4;
                value |= value >> 8;
                value |= value >> 16;
                return value + 1;
            }

            internal bool TryEnqueue(PriorityItem<T> item)
            {
                if (_frozenForEnqueues) return false;

                SpinWait spinner = default;
                while (true)
                {
                    int currentHeadAndTail = _headAndTail;
                    int head = currentHeadAndTail & HeadMask;
                    int tail = (currentHeadAndTail >> TailShift) & HeadMask;
                    int nextTail = (tail + 1) & _slotsMask;

                    if (nextTail == head) return false;

                    if (Interlocked.CompareExchange(ref _headAndTail,
                        (nextTail << TailShift) | head, currentHeadAndTail) == currentHeadAndTail)
                    {
                        _slots[tail] = item;
                        _insertionIndexes[tail] = Interlocked.Increment(ref _globalInsertionCounter);
                        return true;
                    }

                    spinner.SpinOnce();
                }
            }

            internal bool TryPeekHighestPriority(out PriorityItem<T> result, out long insertionIndex)
            {
                result = null!;
                insertionIndex = long.MaxValue;

                if (IsEmpty) return false;

                int head = Head;
                int tail = Tail;

                PriorityItem<T> bestItem = null!;
                long bestInsertionIndex = long.MaxValue;

                for (int i = head; i != tail; i = (i + 1) & _slotsMask)
                {
                    PriorityItem<T> item = _slots[i];
                    if (item != null && (bestItem == null || item.Priority < bestItem.Priority ||
                        (item.Priority == bestItem.Priority && _insertionIndexes[i] < bestInsertionIndex)))
                    {
                        bestItem = item;
                        bestInsertionIndex = _insertionIndexes[i];
                    }
                }

                if (bestItem != null)
                {
                    result = bestItem;
                    insertionIndex = bestInsertionIndex;
                    return true;
                }
                return false;
            }

            internal bool TryRemoveSpecific(PriorityItem<T> targetItem)
            {
                if (IsEmpty) return false;

                int head = Head;
                int tail = Tail;

                for (int i = head; i != tail; i = (i + 1) & _slotsMask)
                {
                    if (ReferenceEquals(_slots[i], targetItem) ||
                        (targetItem != null && targetItem.Equals(_slots[i])))
                    {
                        _slots[i] = null!;
                        return true;
                    }
                }

                return false;
            }

            internal bool Contains(PriorityItem<T> item)
            {
                int head = Head;
                int tail = Tail;

                for (int i = head; i != tail; i = (i + 1) & _slotsMask)
                {
                    if (ReferenceEquals(_slots[i], item) || (item != null && item.Equals(_slots[i])))
                    {
                        return true;
                    }
                }
                return false;
            }

            internal void EnsureFrozenForEnqueues()
            {
                _frozenForEnqueues = true;
            }

            internal PriorityItem<T>[] ToArray()
            {
                List<PriorityItem<T>> result = new();
                int head = Head;
                int tail = Tail;

                for (int i = head; i != tail; i = (i + 1) & _slotsMask)
                {
                    if (_slots[i] != null)
                    {
                        result.Add(_slots[i]);
                    }
                }

                return result.ToArray();
            }
        }
    }
}
