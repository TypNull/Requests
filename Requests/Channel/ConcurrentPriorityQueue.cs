using System.Collections;

namespace Requests.Channel
{
    /// <summary>
    /// Represents a thread-safe priority queue that allows concurrent access.
    /// </summary>
    /// <typeparam name="TElement">The type of elements in the queue.</typeparam>
    public class ConcurrentPriorityQueue<TElement> : IEnumerable<PriorityItem<TElement>>
    {
        private int _count;
        private QueueNode[] _nodes;
        private long _totalEnqueued;
        private readonly bool _autoResize;
        private readonly ReaderWriterLockSlim _lock = new();

        /// <summary>
        /// Initializes a new instance of the <see cref="ConcurrentPriorityQueue{TElement}"/> class.
        /// </summary>
        /// <param name="maxNodes">The maximum number of nodes the queue can hold. If null, the queue will auto-resize.</param>
        /// <exception cref="ArgumentException">Thrown if the queue size is less than or equal to zero.</exception>
        public ConcurrentPriorityQueue(int? maxNodes = null)
        {
            if (maxNodes <= 0) throw new ArgumentException("Queue size must be at least 1.");
            _count = 0;
            _autoResize = maxNodes == null;
            _nodes = new QueueNode[maxNodes ?? 32 + 1];
            _totalEnqueued = 0;
        }

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
        public int Capacity => _nodes.Length - 1;

        /// <summary>
        /// Removes all elements from the <see cref="ConcurrentPriorityQueue{TElement}"/>.
        /// </summary>
        public void Clear()
        {
            _lock.EnterWriteLock();
            try { Array.Clear(_nodes, 1, _count); _count = 0; }
            finally { _lock.ExitWriteLock(); }
        }

        /// <summary>
        /// Determines whether the <see cref="ConcurrentPriorityQueue{TElement}"/> contains a specific <see cref="QueueNode"/>.
        /// </summary>
        /// <param name="node">The <see cref="QueueNode"/> to locate in the queue.</param>
        /// <returns>true if the <see cref="QueueNode"/> is found in the queue; otherwise, false.</returns>
        /// <exception cref="ArgumentNullException">Thrown if the <paramref name="node"/> is null.</exception>
        /// <exception cref="ArgumentException">Thrown if the <paramref name="node"/> index is out of range.</exception>
        public bool Contains(QueueNode node)
        {
            ArgumentNullException.ThrowIfNull(node);
            if (node.Index < 0 || node.Index >= _nodes.Length) throw new ArgumentException("Node index is out of range.");

            _lock.EnterReadLock();
            try { return _nodes[node.Index] == node; }
            finally { _lock.ExitReadLock(); }
        }

        /// <summary>
        /// Adds a <see cref="PriorityItem{TElement}"/> to the <see cref="ConcurrentPriorityQueue{TElement}"/>.
        /// </summary>
        /// <param name="item">The <see cref="PriorityItem{TElement}"/> to add to the queue.</param>
        /// <exception cref="ArgumentNullException">Thrown if the <paramref name="item"/> is null.</exception>
        /// <exception cref="InvalidOperationException">Thrown if the queue is full and auto-resize is disabled.</exception>
        public void Enqueue(PriorityItem<TElement> item)
        {
            ArgumentNullException.ThrowIfNull(item);
            if (_count >= _nodes.Length - 1)
                if (_autoResize) Resize((int)(_count * 1.5));
                else throw new InvalidOperationException("Queue is full.");

            _lock.EnterWriteLock();
            try
            {
                var newNode = new QueueNode(item, Interlocked.Increment(ref _totalEnqueued)) { Index = ++_count };
                _nodes[_count] = newNode;
                BubbleUp(newNode);
            }
            finally { _lock.ExitWriteLock(); }
        }

        /// <summary>
        /// Moves the specified <see cref="QueueNode"/> up the heap if it has a higher priority.
        /// </summary>
        /// <param name="node">The <see cref="QueueNode"/> to move up.</param>
        private void BubbleUp(QueueNode node)
        {
            while (node.Index > 1 && !HasHigherPriority(_nodes[node.Index >> 1], node))
            {
                var parent = _nodes[node.Index >> 1];
                _nodes[node.Index] = parent;
                parent.Index = node.Index;
                node.Index >>= 1;
            }
            _nodes[node.Index] = node;
        }

        /// <summary>
        /// Moves the specified <see cref="QueueNode"/> down the heap if it has a lower priority.
        /// </summary>
        /// <param name="node">The <see cref="QueueNode"/> to move down.</param>
        private void BubbleDown(QueueNode node)
        {
            int finalIndex;
            while ((finalIndex = node.Index) > 0)
            {
                var leftChildIndex = 2 * finalIndex;
                if (leftChildIndex > _count) break;

                var rightChildIndex = leftChildIndex + 1;
                var leftChild = _nodes[leftChildIndex];
                if (HasHigherPriority(leftChild, node))
                {
                    if (rightChildIndex > _count || HasHigherPriority(leftChild, _nodes[rightChildIndex]))
                    {
                        _nodes[finalIndex] = leftChild;
                        leftChild.Index = finalIndex;
                        node.Index = leftChildIndex;
                    }
                    else
                    {
                        var rightChild = _nodes[rightChildIndex];
                        _nodes[finalIndex] = rightChild;
                        rightChild.Index = finalIndex;
                        node.Index = rightChildIndex;
                    }
                }
                else if (rightChildIndex <= _count && HasHigherPriority(_nodes[rightChildIndex], node))
                {
                    var rightChild = _nodes[rightChildIndex];
                    _nodes[finalIndex] = rightChild;
                    rightChild.Index = finalIndex;
                    node.Index = rightChildIndex;
                }
                else break;
            }
            _nodes[node.Index] = node;
        }

        /// <summary>
        /// Determines whether the first <see cref="QueueNode"/> has a higher priority than the second <see cref="QueueNode"/>.
        /// </summary>
        /// <param name="higher">The first <see cref="QueueNode"/> to compare.</param>
        /// <param name="lower">The second <see cref="QueueNode"/> to compare.</param>
        /// <returns>true if the first <see cref="QueueNode"/> has a higher priority; otherwise, false.</returns>
        private static bool HasHigherPriority(QueueNode higher, QueueNode lower) => higher.Priority < lower.Priority || (higher.Priority == lower.Priority && higher.InsertionIndex < lower.InsertionIndex);

        /// <summary>
        /// Removes and returns the <see cref="PriorityItem{TElement}"/> with the highest priority from the <see cref="ConcurrentPriorityQueue{TElement}"/>.
        /// </summary>
        /// <returns>The <see cref="PriorityItem{TElement}"/> with the highest priority.</returns>
        /// <exception cref="InvalidOperationException">Thrown if the queue is empty.</exception>
        public PriorityItem<TElement> Dequeue()
        {
            if (_count <= 0) throw new InvalidOperationException("Cannot dequeue from an empty queue.");

            _lock.EnterWriteLock();
            try
            {
                var returnNode = _nodes[1];
                if (_count == 1) { _nodes[1] = null!; _count = 0; return returnNode; }

                var lastNode = _nodes[_count];
                _nodes[1] = lastNode;
                lastNode.Index = 1;
                _nodes[_count--] = null!;

                BubbleDown(lastNode);
                return returnNode;
            }
            finally { _lock.ExitWriteLock(); }
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

            _lock.EnterWriteLock();
            try
            {
                var newArray = new QueueNode[maxNodes + 1];
                Array.Copy(_nodes, newArray, Math.Min(maxNodes, _count) + 1);
                _nodes = newArray;
            }
            finally { _lock.ExitWriteLock(); }
        }

        /// <summary>
        /// Returns the <see cref="PriorityItem{TElement}"/> with the highest priority in the <see cref="ConcurrentPriorityQueue{TElement}"/> without removing it.
        /// </summary>
        /// <returns>The <see cref="PriorityItem{TElement}"/> with the highest priority.</returns>
        /// <exception cref="InvalidOperationException">Thrown if the queue is empty.</exception>
        public PriorityItem<TElement> Peek()
        {
            if (_count <= 0) throw new InvalidOperationException("Cannot peek at an empty queue.");

            _lock.EnterReadLock();
            try { return _nodes[1]; }
            finally { _lock.ExitReadLock(); }
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
            if (_count >= _nodes.Length - 1)
            {
                if (_autoResize) Resize((int)(_count * 1.5));
                else
                    return false;
            }

            _lock.EnterWriteLock();
            try
            {
                QueueNode node = new(item, Interlocked.Increment(ref _totalEnqueued)) { Index = ++_count };
                _nodes[_count] = node;
                BubbleUp(node);
                return true;
            }
            finally { _lock.ExitWriteLock(); }
        }

        /// <summary>
        /// Attempts to remove and return the <see cref="PriorityItem{TElement}"/> with the highest priority from the <see cref="ConcurrentPriorityQueue{TElement}"/>.
        /// </summary>
        /// <param name="item">When this method returns, contains the <see cref="PriorityItem{TElement}"/> with the highest priority, if the operation succeeded; otherwise, the default value for the type of the <paramref name="item"/> parameter.</param>
        /// <returns>true if the <see cref="PriorityItem{TElement}"/> was removed and returned; otherwise, false.</returns>
        public bool TryDequeue(out PriorityItem<TElement> item)
        {
            if (_count <= 0)
            {
                item = null!;
                return false;
            }

            _lock.EnterWriteLock();
            try
            {
                var returnNode = _nodes[1];
                if (_count == 1) { _nodes[1] = null!; _count = 0; item = returnNode; return true; }

                var lastNode = _nodes[_count];
                _nodes[1] = lastNode;
                lastNode.Index = 1;
                _nodes[_count--] = null!;

                BubbleDown(lastNode);
                item = returnNode;
                return true;
            }
            finally { _lock.ExitWriteLock(); }
        }

        /// <summary>
        /// Attempts to return the <see cref="PriorityItem{TElement}"/> with the highest priority in the <see cref="ConcurrentPriorityQueue{TElement}"/> without removing it.
        /// </summary>
        /// <param name="item">When this method returns, contains the <see cref="PriorityItem{TElement}"/> with the highest priority, if the operation succeeded; otherwise, the default value for the type of the <paramref name="item"/> parameter.</param>
        /// <returns>true if the <see cref="PriorityItem{TElement}"/> was returned; otherwise, false.</returns>
        public bool TryPeek(out PriorityItem<TElement> item)
        {
            if (_count <= 0)
            {
                item = null!;
                return false;
            }

            _lock.EnterReadLock();
            try
            {
                item = _nodes[1];
                return true;
            }
            finally { _lock.ExitReadLock(); }
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
            if (node == null) throw new ArgumentNullException(nameof(node));
            if (!Contains(node)) throw new InvalidOperationException("Cannot update priority on a node that is not in the queue.");

            _lock.EnterWriteLock();
            try
            {
                node.UpdatePriority(priority);
                OnNodeUpdated(node);
            }
            finally { _lock.ExitWriteLock(); }
        }

        /// <summary>
        /// Handles the update of a <see cref="QueueNode"/> by repositioning it in the queue based on its new priority.
        /// </summary>
        /// <param name="node">The <see cref="QueueNode"/> that was updated.</param>
        private void OnNodeUpdated(QueueNode node)
        {
            var parentIndex = node.Index >> 1;
            if (parentIndex > 0 && HasHigherPriority(node, _nodes[parentIndex])) BubbleUp(node);
            else BubbleDown(node);
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
            if (!Contains(node)) throw new InvalidOperationException("Cannot remove a node that is not in the queue.");

            _lock.EnterWriteLock();
            try
            {
                if (node.Index == _count) { _nodes[_count--] = null!; return; }

                var lastNode = _nodes[_count];
                _nodes[node.Index] = lastNode;
                lastNode.Index = node.Index;
                _nodes[_count--] = null!;

                OnNodeUpdated(lastNode);
            }
            finally { _lock.ExitWriteLock(); }
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

            _lock.EnterWriteLock();
            try
            {
                int low = 1;
                int high = _count;

                while (low <= high)
                {
                    int mid = (low + high) / 2;
                    var midNode = _nodes[mid];

                    if (midNode.Item == item)
                    {
                        Remove(midNode);
                        return true;
                    }
                    else if (midNode.Item.Priority < item.Priority)
                    {
                        low = mid + 1;
                    }
                    else
                    {
                        high = mid - 1;
                    }
                }

                return false;
            }
            finally { _lock.ExitWriteLock(); }
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
            if (Contains(node)) throw new InvalidOperationException("Cannot reset a node that is still in the queue.");

            node.Index = 0;
        }

        /// <summary>
        /// Returns an enumerator that iterates through the <see cref="ConcurrentPriorityQueue{TElement}"/>.
        /// </summary>
        /// <returns>An enumerator that can be used to iterate through the queue.</returns>
        public IEnumerator<PriorityItem<TElement>> GetEnumerator()
        {
            _lock.EnterReadLock();
            try { return Enumerable.Range(1, _count).Select(i => _nodes[i].Item).GetEnumerator(); }
            finally { _lock.ExitReadLock(); }
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
            _lock.EnterReadLock();
            try
            {
                return !Enumerable.Range(1, _nodes.Length - 1).Any(i => _nodes[i] != null && ((2 * i < _nodes.Length && HasHigherPriority(_nodes[2 * i], _nodes[i])) || (2 * i + 1 < _nodes.Length && HasHigherPriority(_nodes[2 * i + 1], _nodes[i]))));
            }
            finally { _lock.ExitReadLock(); }
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
            public float Priority { get; private set; }

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
                Priority = item.Priority;
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
    }
}