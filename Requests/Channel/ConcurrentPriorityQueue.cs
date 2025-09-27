using System.Collections;

namespace Requests.Channel
{
    /// <summary>
    /// Represents a thread-safe priority queue that uses a heap-based approach.
    /// Implements O(log n) enqueue/dequeue operations with efficient concurrent access patterns.
    /// </summary>
    /// <typeparam name="TElement">The type of elements in the queue.</typeparam>
    public class ConcurrentPriorityQueue<TElement> : IEnumerable<PriorityItem<TElement>>
    {
        private const int InitialCapacity = 32;
        private const int GrowFactor = 2;
        private const int MinimumGrow = 4;

        /// <summary>Specifies the arity of the d-ary heap.</summary>
        private const int Arity = 4;
        /// <summary>The binary logarithm of <see cref="Arity"/>.</summary>
        private const int Log2Arity = 2;

        /// <summary>Array-backed quaternary min-heap storing priority items.</summary>
        private PriorityItem<TElement>[] _nodes;

        /// <summary>Insertion order tracking for stable priority ordering.</summary>
        private long[] _insertionOrder;

        /// <summary>Lock for synchronizing modifications to the heap structure.</summary>
        private readonly object _lock = new();

        /// <summary>The current number of items in the heap.</summary>
        private volatile int _size;

        /// <summary>Global counter for insertion order tracking.</summary>
        private static long _globalInsertionCounter = 0;

        /// <summary>
        /// Gets the number of elements contained in the <see cref="ConcurrentPriorityQueue{TElement}"/>.
        /// </summary>
        public int Count => _size;

        /// <summary>
        /// Gets a value indicating whether the <see cref="ConcurrentPriorityQueue{TElement}"/> is empty.
        /// </summary>
        public bool IsEmpty => _size == 0;

        /// <summary>
        /// Gets the current capacity of the <see cref="ConcurrentPriorityQueue{TElement}"/>.
        /// </summary>
        public int Capacity => _nodes?.Length ?? 0;

        /// <summary>
        /// Initializes a new instance of the <see cref="ConcurrentPriorityQueue{TElement}"/> class.
        /// </summary>
        /// <param name="initialCapacity">The initial capacity of the queue. If null, uses default capacity.</param>
        /// <exception cref="ArgumentException">Thrown if the capacity is less than or equal to zero.</exception>
        public ConcurrentPriorityQueue(int? initialCapacity = null)
        {
            int capacity = initialCapacity ?? InitialCapacity;
            if (capacity <= 0) throw new ArgumentException("Queue size must be at least 1.", nameof(initialCapacity));

            _nodes = new PriorityItem<TElement>[capacity];
            _insertionOrder = new long[capacity];
            _size = 0;
        }

        /// <summary>
        /// Removes all elements from the <see cref="ConcurrentPriorityQueue{TElement}"/>.
        /// </summary>
        public void Clear()
        {
            lock (_lock)
            {
                Array.Clear(_nodes, 0, _size);
                Array.Clear(_insertionOrder, 0, _size);
                _size = 0;
            }
        }

        /// <summary>
        /// Determines whether the <see cref="ConcurrentPriorityQueue{TElement}"/> contains a specific item.
        /// </summary>
        /// <param name="item">The item to locate in the queue.</param>
        /// <returns>true if the item is found in the queue; otherwise, false.</returns>
        /// <exception cref="ArgumentNullException">Thrown if the <paramref name="item"/> is null.</exception>
        public bool Contains(PriorityItem<TElement> item)
        {
            ArgumentNullException.ThrowIfNull(item);

            lock (_lock)
            {
                for (int i = 0; i < _size; i++)
                {
                    if (ReferenceEquals(_nodes[i], item) ||
                        (item != null && item.Equals(_nodes[i])))
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        /// <summary>
        /// Adds a <see cref="PriorityItem{TElement}"/> to the <see cref="ConcurrentPriorityQueue{TElement}"/>.
        /// Uses heap operations for O(log n).
        /// </summary>
        /// <param name="item">The <see cref="PriorityItem{TElement}"/> to add to the queue.</param>
        /// <exception cref="ArgumentNullException">Thrown if the <paramref name="item"/> is null.</exception>
        public void Enqueue(PriorityItem<TElement> item)
        {
            ArgumentNullException.ThrowIfNull(item);

            lock (_lock)
            {
                int currentSize = _size;

                // Ensure capacity
                if (_nodes.Length == currentSize)
                {
                    Grow(currentSize + 1);
                }

                // Add item to the end and bubble up to maintain heap property
                long insertionIndex = Interlocked.Increment(ref _globalInsertionCounter);
                _insertionOrder[currentSize] = insertionIndex;
                _nodes[currentSize] = item;
                _size = currentSize + 1;

                // Restore heap property by moving the new item up
                MoveUp(currentSize);
            }
        }

        /// <summary>
        /// Removes and returns the <see cref="PriorityItem{TElement}"/> with the highest priority.
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
        /// Attempts to remove and return the <see cref="PriorityItem{TElement}"/> with the highest priority.
        /// Uses heap operations for O(log n).
        /// </summary>
        /// <param name="item">When this method returns, contains the highest priority item if successful.</param>
        /// <returns>true if an item was successfully removed; otherwise, false.</returns>
        public bool TryDequeue(out PriorityItem<TElement> item)
        {
            lock (_lock)
            {
                if (_size == 0)
                {
                    item = null!;
                    return false;
                }

                // Get the root (highest priority item)
                item = _nodes[0];

                // Move the last item to root and restore heap property
                RemoveRootNode();
                return true;
            }
        }

        /// <summary>
        /// Returns the <see cref="PriorityItem{TElement}"/> with the highest priority without removing it.
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
        /// Attempts to return the <see cref="PriorityItem{TElement}"/> with the highest priority without removing it.
        /// </summary>
        /// <param name="item">When this method returns, contains the highest priority item if successful.</param>
        /// <returns>true if an item was found; otherwise, false.</returns>
        public bool TryPeek(out PriorityItem<TElement> item)
        {
            lock (_lock)
            {
                if (_size == 0)
                {
                    item = null!;
                    return false;
                }

                item = _nodes[0];
                return true;
            }
        }

        /// <summary>
        /// Attempts to add a <see cref="PriorityItem{TElement}"/> to the queue.
        /// </summary>
        /// <param name="item">The item to add.</param>
        /// <returns>true if the item was added successfully; otherwise, false.</returns>
        public bool TryEnqueue(PriorityItem<TElement> item)
        {
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
        /// Attempts to remove a specific <see cref="PriorityItem{TElement}"/> from the queue.
        /// </summary>
        /// <param name="item">The item to remove.</param>
        /// <returns>true if the item was removed; otherwise, false.</returns>
        /// <exception cref="ArgumentNullException">Thrown if the <paramref name="item"/> is null.</exception>
        public bool TryRemove(PriorityItem<TElement> item)
        {
            ArgumentNullException.ThrowIfNull(item);

            lock (_lock)
            {
                // Find the item in the heap
                int index = FindIndex(item);
                if (index < 0) return false;

                // Remove the item at the found index
                RemoveAt(index);
                return true;
            }
        }

        /// <summary>
        /// Copies the elements stored in the queue to a new array.
        /// </summary>
        /// <returns>A new array containing a snapshot of elements from the queue.</returns>
        public PriorityItem<TElement>[] ToArray()
        {
            lock (_lock)
            {
                if (_size == 0) return Array.Empty<PriorityItem<TElement>>();

                PriorityItem<TElement>[] result = new PriorityItem<TElement>[_size];
                Array.Copy(_nodes, 0, result, 0, _size);

                // Sort by priority, then by insertion order for stability
                Array.Sort(result, (a, b) =>
                {
                    int priorityComparison = a.Priority.CompareTo(b.Priority);
                    if (priorityComparison != 0) return priorityComparison;

                    // For items with same priority, maintain insertion order
                    int aIndex = FindInsertionIndex(a);
                    int bIndex = FindInsertionIndex(b);
                    return aIndex.CompareTo(bIndex);
                });

                return result;
            }
        }

        /// <summary>
        /// Validates the internal structure of the queue to ensure it maintains heap property.
        /// </summary>
        /// <returns>true if the queue is a valid heap; otherwise, false.</returns>
        public bool IsValidQueue()
        {
            lock (_lock)
            {
                for (int i = 0; i < _size; i++)
                {
                    int leftChild = GetFirstChildIndex(i);
                    int rightBound = Math.Min(leftChild + Arity, _size);

                    for (int child = leftChild; child < rightBound; child++)
                    {
                        if (CompareNodes(i, child) > 0)
                        {
                            return false; // Heap property violated
                        }
                    }
                }
                return true;
            }
        }

        /// <summary>
        /// Returns an enumerator that iterates through the queue in heap order (not priority order).
        /// </summary>
        /// <returns>An enumerator for the queue.</returns>
        public IEnumerator<PriorityItem<TElement>> GetEnumerator()
        {
            PriorityItem<TElement>[] snapshot;
            lock (_lock)
            {
                snapshot = new PriorityItem<TElement>[_size];
                Array.Copy(_nodes, 0, snapshot, 0, _size);
            }

            foreach (PriorityItem<TElement> item in snapshot)
            {
                if (item != null) yield return item;
            }
        }

        /// <summary>
        /// Returns an enumerator that iterates through the queue.
        /// </summary>
        /// <returns>An enumerator for the queue.</returns>
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        #region Private Helper Methods

        /// <summary>
        /// Grows the capacity of the internal arrays to accommodate more items.
        /// </summary>
        /// <param name="minCapacity">The minimum required capacity.</param>
        private void Grow(int minCapacity)
        {
            int oldCapacity = _nodes.Length;
            int newCapacity = Math.Max(oldCapacity * GrowFactor, oldCapacity + MinimumGrow);

            if (newCapacity < minCapacity)
                newCapacity = minCapacity;

            Array.Resize(ref _nodes, newCapacity);
            Array.Resize(ref _insertionOrder, newCapacity);
        }

        /// <summary>
        /// Removes the root node and restores heap property.
        /// </summary>
        private void RemoveRootNode()
        {
            int lastIndex = --_size;
            if (lastIndex > 0)
            {
                // Move last element to root and restore heap property
                _nodes[0] = _nodes[lastIndex];
                _insertionOrder[0] = _insertionOrder[lastIndex];
                MoveDown(0);
            }

            // Clear the last position
            _nodes[lastIndex] = null!;
            _insertionOrder[lastIndex] = 0;
        }

        /// <summary>
        /// Removes the item at the specified index and restores heap property.
        /// </summary>
        /// <param name="index">The index of the item to remove.</param>
        private void RemoveAt(int index)
        {
            int lastIndex = --_size;

            if (index < lastIndex)
            {
                // Move last element to the removed position
                PriorityItem<TElement> lastNode = _nodes[lastIndex];
                long lastOrder = _insertionOrder[lastIndex];
                PriorityItem<TElement> removedNode = _nodes[index];
                long removedOrder = _insertionOrder[index];

                _nodes[index] = lastNode;
                _insertionOrder[index] = lastOrder;

                // Determine whether to move up or down
                if (CompareByPriorityAndOrder(lastNode, lastOrder, removedNode, removedOrder) < 0)
                {
                    MoveUp(index);
                }
                else
                {
                    MoveDown(index);
                }
            }

            // Clear the last position
            _nodes[lastIndex] = null!;
            _insertionOrder[lastIndex] = 0;
        }

        /// <summary>
        /// Moves an item up the heap to restore heap property.
        /// </summary>
        /// <param name="nodeIndex">The index of the node to move up.</param>
        private void MoveUp(int nodeIndex)
        {
            PriorityItem<TElement> node = _nodes[nodeIndex];
            long order = _insertionOrder[nodeIndex];

            while (nodeIndex > 0)
            {
                int parentIndex = GetParentIndex(nodeIndex);

                if (CompareByPriorityAndOrder(node, order, _nodes[parentIndex], _insertionOrder[parentIndex]) >= 0)
                    break;

                // Move parent down
                _nodes[nodeIndex] = _nodes[parentIndex];
                _insertionOrder[nodeIndex] = _insertionOrder[parentIndex];
                nodeIndex = parentIndex;
            }

            _nodes[nodeIndex] = node;
            _insertionOrder[nodeIndex] = order;
        }

        /// <summary>
        /// Moves an item down the heap to restore heap property.
        /// </summary>
        /// <param name="nodeIndex">The index of the node to move down.</param>
        private void MoveDown(int nodeIndex)
        {
            PriorityItem<TElement> node = _nodes[nodeIndex];
            long order = _insertionOrder[nodeIndex];

            int i;
            while ((i = GetFirstChildIndex(nodeIndex)) < _size)
            {
                // Find the child with minimal priority
                int minChildIndex = i;
                PriorityItem<TElement> minChild = _nodes[i];
                long minChildOrder = _insertionOrder[i];

                int childUpperBound = Math.Min(i + Arity, _size);
                for (int child = i + 1; child < childUpperBound; child++)
                {
                    if (CompareByPriorityAndOrder(_nodes[child], _insertionOrder[child], minChild, minChildOrder) < 0)
                    {
                        minChildIndex = child;
                        minChild = _nodes[child];
                        minChildOrder = _insertionOrder[child];
                    }
                }

                // If heap property is satisfied, stop
                if (CompareByPriorityAndOrder(node, order, minChild, minChildOrder) <= 0)
                    break;

                // Move minimal child up
                _nodes[nodeIndex] = minChild;
                _insertionOrder[nodeIndex] = minChildOrder;
                nodeIndex = minChildIndex;
            }

            _nodes[nodeIndex] = node;
            _insertionOrder[nodeIndex] = order;
        }

        /// <summary>
        /// Compares two nodes in the heap for ordering.
        /// </summary>
        /// <param name="indexA">Index of first node.</param>
        /// <param name="indexB">Index of second node.</param>
        /// <returns>Negative if A has higher priority, positive if B has higher priority, zero if equal.</returns>
        private int CompareNodes(int indexA, int indexB)
        {
            return CompareByPriorityAndOrder(
                _nodes[indexA], _insertionOrder[indexA],
                _nodes[indexB], _insertionOrder[indexB]);
        }

        /// <summary>
        /// Compares two items by priority and insertion order.
        /// </summary>
        /// <param name="itemA">First item.</param>
        /// <param name="orderA">First item's insertion order.</param>
        /// <param name="itemB">Second item.</param>
        /// <param name="orderB">Second item's insertion order.</param>
        /// <returns>Comparison result for heap ordering.</returns>
        private static int CompareByPriorityAndOrder(PriorityItem<TElement> itemA, long orderA, PriorityItem<TElement> itemB, long orderB)
        {
            // Lower priority value = higher priority (min-heap for priorities)
            int priorityComparison = itemA.Priority.CompareTo(itemB.Priority);
            if (priorityComparison != 0)
                return priorityComparison;

            // For equal priorities, use insertion order (FIFO for same priority)
            return orderA.CompareTo(orderB);
        }

        /// <summary>
        /// Finds the index of a specific item in the heap.
        /// </summary>
        /// <param name="item">The item to find.</param>
        /// <returns>The index of the item, or -1 if not found.</returns>
        private int FindIndex(PriorityItem<TElement> item)
        {
            for (int i = 0; i < _size; i++)
            {
                if (ReferenceEquals(_nodes[i], item) ||
                    (item != null && item.Equals(_nodes[i])))
                {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>
        /// Finds the insertion index for a given item (used for sorting).
        /// </summary>
        /// <param name="item">The item to find insertion index for.</param>
        /// <returns>The insertion index, or -1 if not found.</returns>
        private int FindInsertionIndex(PriorityItem<TElement> item)
        {
            for (int i = 0; i < _size; i++)
            {
                if (ReferenceEquals(_nodes[i], item) ||
                    (item != null && item.Equals(_nodes[i])))
                {
                    return (int)_insertionOrder[i];
                }
            }
            return -1;
        }

        /// <summary>
        /// Gets the index of an element's parent in the heap.
        /// </summary>
        /// <param name="index">The child index.</param>
        /// <returns>The parent index.</returns>
        private static int GetParentIndex(int index) => (index - 1) >> Log2Arity;

        /// <summary>
        /// Gets the index of the first child of an element in the heap.
        /// </summary>
        /// <param name="index">The parent index.</param>
        /// <returns>The first child index.</returns>
        private static int GetFirstChildIndex(int index) => (index << Log2Arity) + 1;

        #endregion
    }
}