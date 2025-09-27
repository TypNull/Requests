using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license

namespace Requests.Channel
{
    /// <summary>
    /// Represents a thread-safe, concurrent queue data structure.
    /// </summary>
    [DebuggerDisplay("Count = {Count}")]
    public class ConcurrentQueue<T> : IProducerConsumerCollection<T>, IReadOnlyCollection<T>
    {
        private const int InitialSegmentLength = 32;
        private const int MaxSegmentLength = 1024 * 1024;

        private readonly object _crossSegmentLock;
        private volatile ConcurrentQueueSegment _tail;
        private volatile ConcurrentQueueSegment _head;

        /// <summary>
        /// Initializes a new instance of the <see cref="ConcurrentQueue{T}"/> class.
        /// </summary>
        public ConcurrentQueue()
        {
            _crossSegmentLock = new object();
            _tail = _head = new ConcurrentQueueSegment(InitialSegmentLength);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ConcurrentQueue{T}"/> class that contains elements copied from the specified collection.
        /// </summary>
        /// <param name="collection">The collection whose elements are copied to the new <see cref="ConcurrentQueue{T}"/>.</param>
        /// <exception cref="ArgumentNullException">Thrown when the <paramref name="collection"/> is null.</exception>
        public ConcurrentQueue(IEnumerable<T> collection)
        {
            if (collection == null)
            {
                throw new ArgumentNullException(nameof(collection));
            }

            _crossSegmentLock = new object();

            int length = InitialSegmentLength;
            if (collection is ICollection<T> c)
            {
                int count = c.Count;
                if (count > length)
                {
                    length = (int)Math.Min(RoundUpToPowerOf2((uint)count), MaxSegmentLength);
                }
            }

            _tail = _head = new ConcurrentQueueSegment(length);
            foreach (T item in collection)
            {
                Enqueue(item);
            }
        }

        /// <summary>
        /// Attempts to add an item to the <see cref="ConcurrentQueue{T}"/>.
        /// </summary>
        /// <param name="item">The item to add to the <see cref="ConcurrentQueue{T}"/>.</param>
        /// <returns>Always returns true.</returns>
        bool IProducerConsumerCollection<T>.TryAdd(T item)
        {
            Enqueue(item);
            return true;
        }

        /// <summary>
        /// Attempts to remove and return an item from the <see cref="ConcurrentQueue{T}"/>.
        /// </summary>
        /// <param name="item">When this method returns, if the operation was successful, <paramref name="item"/> contains the removed item. If no item was available to be removed, the value is unspecified.</param>
        /// <returns>true if an element was removed and returned from the queue successfully; otherwise, false.</returns>
        bool IProducerConsumerCollection<T>.TryTake([MaybeNullWhen(false)] out T item) => TryDequeue(out item);

        /// <summary>
        /// Gets a value that indicates whether the <see cref="ConcurrentQueue{T}"/> is empty.
        /// </summary>
        public bool IsEmpty => !TryPeek(out _, resultUsed: false);

        /// <summary>
        /// Copies the elements of the <see cref="ConcurrentQueue{T}"/> to a new array.
        /// </summary>
        /// <returns>A new array containing a snapshot of elements copied from the <see cref="ConcurrentQueue{T}"/>.</returns>
        public T[] ToArray()
        {
            SnapForObservation(out ConcurrentQueueSegment head, out int headHead, out ConcurrentQueueSegment tail, out int tailTail);

            long count = GetCount(head, headHead, tail, tailTail);
            T[] arr = new T[count];

            using (IEnumerator<T> e = Enumerate(head, headHead, tail, tailTail))
            {
                int i = 0;
                while (e.MoveNext())
                {
                    arr[i++] = e.Current;
                }
                Debug.Assert(count == i);
            }

            return arr;
        }

        /// <summary>
        /// Gets the number of elements contained in the <see cref="ConcurrentQueue{T}"/>.
        /// </summary>
        public int Count
        {
            get
            {
                SpinWait spinner = default;
                while (true)
                {
                    ConcurrentQueueSegment head = _head;
                    ConcurrentQueueSegment tail = _tail;
                    int headHead = Volatile.Read(ref head._headAndTail.Head);
                    int headTail = Volatile.Read(ref head._headAndTail.Tail);

                    if (head == tail)
                    {
                        if (head == _head &&
                            tail == _tail &&
                            headHead == Volatile.Read(ref head._headAndTail.Head) &&
                            headTail == Volatile.Read(ref head._headAndTail.Tail))
                        {
                            return GetCount(head, headHead, headTail);
                        }
                    }
                    else if (head._nextSegment == tail)
                    {
                        int tailHead = Volatile.Read(ref tail._headAndTail.Head);
                        int tailTail = Volatile.Read(ref tail._headAndTail.Tail);
                        if (head == _head &&
                            tail == _tail &&
                            headHead == Volatile.Read(ref head._headAndTail.Head) &&
                            headTail == Volatile.Read(ref head._headAndTail.Tail) &&
                            tailHead == Volatile.Read(ref tail._headAndTail.Head) &&
                            tailTail == Volatile.Read(ref tail._headAndTail.Tail))
                        {
                            return GetCount(head, headHead, headTail) + GetCount(tail, tailHead, tailTail);
                        }
                    }
                    else
                    {
                        lock (_crossSegmentLock)
                        {
                            if (head == _head && tail == _tail)
                            {
                                int tailHead = Volatile.Read(ref tail._headAndTail.Head);
                                int tailTail = Volatile.Read(ref tail._headAndTail.Tail);
                                if (headHead == Volatile.Read(ref head._headAndTail.Head) &&
                                    headTail == Volatile.Read(ref head._headAndTail.Tail) &&
                                    tailHead == Volatile.Read(ref tail._headAndTail.Head) &&
                                    tailTail == Volatile.Read(ref tail._headAndTail.Tail))
                                {
                                    int count = GetCount(head, headHead, headTail) + GetCount(tail, tailHead, tailTail);

                                    for (ConcurrentQueueSegment s = head._nextSegment!; s != tail; s = s._nextSegment!)
                                    {
                                        count += s._headAndTail.Tail - s.FreezeOffset;
                                    }

                                    return count;
                                }
                            }
                        }
                    }

                    spinner.SpinOnce();
                }
            }
        }

        /// <summary>
        /// Gets a value indicating whether access to the <see cref="ConcurrentQueue{T}"/> is synchronized (thread safe).
        /// </summary>
        public bool IsSynchronized => false;

        /// <summary>
        /// Gets an object that can be used to synchronize access to the <see cref="ConcurrentQueue{T}"/>.
        /// </summary>
        /// <exception cref="NotSupportedException">Concurrent collections do not support a SyncRoot.</exception>
        public object SyncRoot => throw new NotSupportedException("Concurrent collections do not support a SyncRoot.");

        /// <summary>
        /// Gets the number of elements in a specific <see cref="ConcurrentQueueSegment"/>.
        /// </summary>
        /// <param name="s">The segment to count elements in.</param>
        /// <param name="head">The head index of the segment.</param>
        /// <param name="tail">The tail index of the segment.</param>
        /// <returns>The number of elements in the segment.</returns>
        private static int GetCount(ConcurrentQueueSegment s, int head, int tail)
        {
            if (head != tail && head != tail - s.FreezeOffset)
            {
                head &= s._slotsMask;
                tail &= s._slotsMask;
                return head < tail ? tail - head : s._slots.Length - head + tail;
            }
            return 0;
        }

        /// <summary>
        /// Gets the total number of elements in the queue by counting elements in multiple segments.
        /// </summary>
        /// <param name="head">The head segment of the queue.</param>
        /// <param name="headHead">The head index of the head segment.</param>
        /// <param name="tail">The tail segment of the queue.</param>
        /// <param name="tailTail">The tail index of the tail segment.</param>
        /// <returns>The total number of elements in the queue.</returns>
        private static long GetCount(ConcurrentQueueSegment head, int headHead, ConcurrentQueueSegment tail, int tailTail)
        {
            long count = 0;

            int headTail = (head == tail ? tailTail : Volatile.Read(ref head._headAndTail.Tail)) - head.FreezeOffset;
            if (headHead < headTail)
            {
                headHead &= head._slotsMask;
                headTail &= head._slotsMask;

                count += headHead < headTail ?
                    headTail - headHead :
                    head._slots.Length - headHead + headTail;
            }

            if (head != tail)
            {
                for (ConcurrentQueueSegment s = head._nextSegment!; s != tail; s = s._nextSegment!)
                {
                    count += s._headAndTail.Tail - s.FreezeOffset;
                }

                count += tailTail - tail.FreezeOffset;
            }

            return count;
        }

        /// <summary>
        /// Copies the elements of the <see cref="ConcurrentQueue{T}"/> to an <see cref="Array"/>, starting at a particular <see cref="Array"/> index.
        /// </summary>
        /// <param name="array">The one-dimensional <see cref="Array"/> that is the destination of the elements copied from <see cref="ConcurrentQueue{T}"/>. The <see cref="Array"/> must have zero-based indexing.</param>
        /// <param name="index">The zero-based index in <paramref name="array"/> at which copying begins.</param>
        /// <exception cref="ArgumentNullException">Thrown when the <paramref name="array"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the <paramref name="index"/> is less than zero.</exception>
        /// <exception cref="ArgumentException">Thrown when the <paramref name="array"/> plus <paramref name="index"/> is too small to contain all elements of the <see cref="ConcurrentQueue{T}"/>.</exception>
        public void CopyTo(T[] array, int index)
        {
            if (array == null)
            {
                throw new ArgumentNullException(nameof(array));
            }
            if (index < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            SnapForObservation(out ConcurrentQueueSegment head, out int headHead, out ConcurrentQueueSegment tail, out int tailTail);

            long count = GetCount(head, headHead, tail, tailTail);
            if (index > array.Length - count)
            {
                throw new ArgumentException("The array plus offset is too small.");
            }

            int i = index;
            using (IEnumerator<T> e = Enumerate(head, headHead, tail, tailTail))
            {
                while (e.MoveNext())
                {
                    array[i++] = e.Current;
                }
            }
            Debug.Assert(count == i - index);
        }

        /// <summary>
        /// Returns an enumerator that iterates through the <see cref="ConcurrentQueue{T}"/>.
        /// </summary>
        /// <returns>An enumerator for the <see cref="ConcurrentQueue{T}"/>.</returns>
        public IEnumerator<T> GetEnumerator()
        {
            SnapForObservation(out ConcurrentQueueSegment head, out int headHead, out ConcurrentQueueSegment tail, out int tailTail);
            return Enumerate(head, headHead, tail, tailTail);
        }

        /// <summary>
        /// Takes a snapshot of the current state of the queue for observation purposes.
        /// </summary>
        /// <param name="head">The head segment of the queue.</param>
        /// <param name="headHead">The head index of the head segment.</param>
        /// <param name="tail">The tail segment of the queue.</param>
        /// <param name="tailTail">The tail index of the tail segment.</param>
        private void SnapForObservation(out ConcurrentQueueSegment head, out int headHead, out ConcurrentQueueSegment tail, out int tailTail)
        {
            lock (_crossSegmentLock)
            {
                head = _head;
                tail = _tail;
                Debug.Assert(head != null);
                Debug.Assert(tail != null);
                Debug.Assert(tail._nextSegment == null);

                for (ConcurrentQueueSegment s = head; ; s = s._nextSegment!)
                {
                    s._preservedForObservation = true;
                    if (s == tail) break;
                    Debug.Assert(s._frozenForEnqueues);
                }
                tail.EnsureFrozenForEnqueues();

                headHead = Volatile.Read(ref head._headAndTail.Head);
                tailTail = Volatile.Read(ref tail._headAndTail.Tail);
            }
        }

        /// <summary>
        /// Gets an item from the specified segment when it becomes available.
        /// </summary>
        /// <param name="segment">The segment to retrieve the item from.</param>
        /// <param name="i">The index of the item in the segment.</param>
        /// <returns>The item at the specified index.</returns>
        private static T GetItemWhenAvailable(ConcurrentQueueSegment segment, int i)
        {
            Debug.Assert(segment._preservedForObservation);

            int expectedSequenceNumberAndMask = (i + 1) & segment._slotsMask;

            if ((segment._slots[i].SequenceNumber & segment._slotsMask) != expectedSequenceNumberAndMask)
            {
                SpinWait spinner = default;
                while ((Volatile.Read(ref segment._slots[i].SequenceNumber) & segment._slotsMask) != expectedSequenceNumberAndMask)
                {
                    spinner.SpinOnce();
                }
            }

            return segment._slots[i].Item!;
        }

        /// <summary>
        /// Enumerates the elements of the queue from the specified segments.
        /// </summary>
        /// <param name="head">The head segment of the queue.</param>
        /// <param name="headHead">The head index of the head segment.</param>
        /// <param name="tail">The tail segment of the queue.</param>
        /// <param name="tailTail">The tail index of the tail segment.</param>
        /// <returns>An enumerator for the elements in the specified segments.</returns>
        private static IEnumerator<T> Enumerate(ConcurrentQueueSegment head, int headHead, ConcurrentQueueSegment tail, int tailTail)
        {
            Debug.Assert(head._preservedForObservation);
            Debug.Assert(head._frozenForEnqueues);
            Debug.Assert(tail._preservedForObservation);
            Debug.Assert(tail._frozenForEnqueues);

            int headTail = (head == tail ? tailTail : Volatile.Read(ref head._headAndTail.Tail)) - head.FreezeOffset;
            if (headHead < headTail)
            {
                headHead &= head._slotsMask;
                headTail &= head._slotsMask;

                if (headHead < headTail)
                {
                    for (int i = headHead; i < headTail; i++) yield return GetItemWhenAvailable(head, i);
                }
                else
                {
                    for (int i = headHead; i < head._slots.Length; i++) yield return GetItemWhenAvailable(head, i);
                    for (int i = 0; i < headTail; i++) yield return GetItemWhenAvailable(head, i);
                }
            }

            if (head != tail)
            {
                for (ConcurrentQueueSegment s = head._nextSegment!; s != tail; s = s._nextSegment!)
                {
                    Debug.Assert(s._preservedForObservation);
                    Debug.Assert(s._frozenForEnqueues);

                    int sTail = s._headAndTail.Tail - s.FreezeOffset;
                    for (int i = 0; i < sTail; i++)
                    {
                        yield return GetItemWhenAvailable(s, i);
                    }
                }

                tailTail -= tail.FreezeOffset;
                for (int i = 0; i < tailTail; i++)
                {
                    yield return GetItemWhenAvailable(tail, i);
                }
            }
        }

        /// <summary>
        /// Adds an object to the end of the <see cref="ConcurrentQueue{T}"/>.
        /// </summary>
        /// <param name="item">The object to add to the <see cref="ConcurrentQueue{T}"/>. The value can be a null reference (Nothing in Visual Basic) for reference types.</param>
        public void Enqueue(T item)
        {
            if (!_tail.TryEnqueue(item))
            {
                EnqueueSlow(item);
            }
        }

        /// <summary>
        /// Adds an object to the end of the <see cref="ConcurrentQueue{T}"/> using a slower path when the tail segment is full.
        /// </summary>
        /// <param name="item">The object to add to the <see cref="ConcurrentQueue{T}"/>. The value can be a null reference (Nothing in Visual Basic) for reference types.</param>
        private void EnqueueSlow(T item)
        {
            while (true)
            {
                ConcurrentQueueSegment tail = _tail;

                if (tail.TryEnqueue(item))
                {
                    return;
                }

                lock (_crossSegmentLock)
                {
                    if (tail == _tail)
                    {
                        tail.EnsureFrozenForEnqueues();

                        int nextSize = tail._preservedForObservation ? InitialSegmentLength : Math.Min(tail.Capacity * 2, MaxSegmentLength);
                        ConcurrentQueueSegment newTail = new(nextSize);

                        tail._nextSegment = newTail;
                        _tail = newTail;
                    }
                }
            }
        }

        /// <summary>
        /// Attempts to remove and return the object at the beginning of the <see cref="ConcurrentQueue{T}"/>.
        /// </summary>
        /// <param name="result">When this method returns, if the operation was successful, <paramref name="result"/> contains the object removed. If no object was available to be removed, the value is unspecified.</param>
        /// <returns>true if an element was removed and returned from the beginning of the <see cref="ConcurrentQueue{T}"/> successfully; otherwise, false.</returns>
        public bool TryDequeue([MaybeNullWhen(false)] out T result)
        {
            ConcurrentQueueSegment head = _head;

            if (head.TryDequeue(out result))
            {
                return true;
            }

            if (head._nextSegment == null)
            {
                result = default!;
                return false;
            }

            return TryDequeueSlow(out result);
        }

        /// <summary>
        /// Attempts to remove and return the object at the beginning of the <see cref="ConcurrentQueue{T}"/> using a slower path when the head segment is empty.
        /// </summary>
        /// <param name="item">When this method returns, if the operation was successful, <paramref name="item"/> contains the removed object. If no object was available to be removed, the value is unspecified.</param>
        /// <returns>true if an element was removed and returned from the beginning of the <see cref="ConcurrentQueue{T}"/> successfully; otherwise, false.</returns>
        private bool TryDequeueSlow([MaybeNullWhen(false)] out T item)
        {
            while (true)
            {
                ConcurrentQueueSegment head = _head;

                if (head.TryDequeue(out item))
                {
                    return true;
                }

                if (head._nextSegment == null)
                {
                    item = default;
                    return false;
                }

                Debug.Assert(head._frozenForEnqueues);
                if (head.TryDequeue(out item))
                {
                    return true;
                }

                lock (_crossSegmentLock)
                {
                    if (head == _head)
                    {
                        _head = head._nextSegment;
                    }
                }
            }
        }

        /// <summary>
        /// Attempts to return an object from the beginning of the <see cref="ConcurrentQueue{T}"/> without removing it.
        /// </summary>
        /// <param name="result">When this method returns, <paramref name="result"/> contains an object from the beginning of the <see cref="ConcurrentQueue{T}"/> or an unspecified value if the operation failed.</param>
        /// <returns>true if an object was returned successfully; otherwise, false.</returns>
        public bool TryPeek([MaybeNullWhen(false)] out T result) => TryPeek(out result, resultUsed: true);

        /// <summary>
        /// Attempts to return an object from the beginning of the <see cref="ConcurrentQueue{T}"/> without removing it.
        /// </summary>
        /// <param name="result">When this method returns, <paramref name="result"/> contains an object from the beginning of the <see cref="ConcurrentQueue{T}"/> or an unspecified value if the operation failed.</param>
        /// <param name="resultUsed">Indicates whether the result is used.</param>
        /// <returns>true if an object was returned successfully; otherwise, false.</returns>
        private bool TryPeek([MaybeNullWhen(false)] out T result, bool resultUsed)
        {
            ConcurrentQueueSegment s = _head;
            while (true)
            {
                ConcurrentQueueSegment? next = Volatile.Read(ref s._nextSegment);

                if (s.TryPeek(out result, resultUsed))
                {
                    return true;
                }

                if (next != null)
                {
                    Debug.Assert(next == s._nextSegment);
                    s = next;
                }
                else if (Volatile.Read(ref s._nextSegment) == null)
                {
                    break;
                }
            }

            result = default;
            return false;
        }

        /// <summary>
        /// Removes all objects from the <see cref="ConcurrentQueue{T}"/>.
        /// </summary>
        public void Clear()
        {
            lock (_crossSegmentLock)
            {
                _tail.EnsureFrozenForEnqueues();
                _tail = _head = new ConcurrentQueueSegment(InitialSegmentLength);
            }
        }

        /// <summary>
        /// Rounds up the specified value to the nearest power of 2.
        /// </summary>
        /// <param name="v">The value to round up.</param>
        /// <returns>The nearest power of 2 that is greater than or equal to <paramref name="v"/>.</returns>
        private static uint RoundUpToPowerOf2(uint v)
        {
            v--;
            v |= v >> 1;
            v |= v >> 2;
            v |= v >> 4;
            v |= v >> 8;
            v |= v >> 16;
            v++;
            return v;
        }

        /// <summary>
        /// Copies the elements of the <see cref="ConcurrentQueue{T}"/> to an <see cref="Array"/>, starting at a particular <see cref="Array"/> index.
        /// </summary>
        /// <param name="array">The one-dimensional <see cref="Array"/> that is the destination of the elements copied from <see cref="ConcurrentQueue{T}"/>. The <see cref="Array"/> must have zero-based indexing.</param>
        /// <param name="index">The zero-based index in <paramref name="array"/> at which copying begins.</param>
        /// <exception cref="ArgumentNullException">Thrown when the <paramref name="array"/> is null.</exception>
        public void CopyTo(Array array, int index)
        {
            if (array is T[] szArray)
            {
                CopyTo(szArray, index);
                return;
            }

            if (array == null)
            {
                throw new ArgumentNullException(nameof(array));
            }

            ToArray().CopyTo(array, index);
        }

        /// <summary>
        /// Attempts to remove a specific item from the queue.
        /// </summary>
        /// <param name="item">The item to remove.</param>
        /// <returns>true if the item was found and removed; otherwise, false.</returns>
        public bool TryRemove(T item)
        {
            ConcurrentQueueSegment tail = _tail;

            if (tail.TryRemoveItem(item))
                return true;

            ConcurrentQueueSegment? current = tail;
            while (current != null && current != _head)
            {
                ConcurrentQueueSegment? prev = _head;
                while (prev != null && prev._nextSegment != current)
                    prev = prev._nextSegment;

                if (prev != null && prev.TryRemoveItem(item))
                    return true;

                current = prev;
            }
            return _head.TryRemoveItem(item);
        }

        /// <summary>
        /// Returns an enumerator that iterates through the <see cref="ConcurrentQueue{T}"/>.
        /// </summary>
        /// <returns>An enumerator for the <see cref="ConcurrentQueue{T}"/>.</returns>
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => ((IEnumerable<T>)this).GetEnumerator();

        /// <summary>
        /// Represents a segment of the <see cref="ConcurrentQueue{T}"/>.
        /// </summary>
        [DebuggerDisplay("Capacity = {Capacity}")]
        internal sealed class ConcurrentQueueSegment
        {
            internal readonly Slot[] _slots;
            internal readonly int _slotsMask;
            internal PaddedHeadAndTail _headAndTail;

            internal bool _preservedForObservation;
            internal bool _frozenForEnqueues;
            internal ConcurrentQueueSegment? _nextSegment;

            // Flag to mark removed items in sequence number
            private const int REMOVED_FLAG = unchecked((int)0x80000000);

            /// <summary>
            /// Initializes a new instance of the <see cref="ConcurrentQueueSegment"/> class with the specified bounded length.
            /// </summary>
            /// <param name="boundedLength">The bounded length of the segment.</param>
            internal ConcurrentQueueSegment(int boundedLength)
            {
                Debug.Assert(boundedLength >= 2, $"Must be >= 2, got {boundedLength}");
                Debug.Assert((boundedLength & (boundedLength - 1)) == 0, $"Must be a power of 2, got {boundedLength}");

                _slots = new Slot[boundedLength];
                _slotsMask = boundedLength - 1;

                for (int i = 0; i < _slots.Length; i++)
                {
                    _slots[i].SequenceNumber = i;
                }
            }

            /// <summary>
            /// Gets the capacity of the segment.
            /// </summary>
            internal int Capacity => _slots.Length;

            /// <summary>
            /// Gets the freeze offset of the segment.
            /// </summary>
            internal int FreezeOffset => _slots.Length * 2;

            /// <summary>
            /// Ensures that the segment is frozen for enqueues.
            /// </summary>
            internal void EnsureFrozenForEnqueues()
            {
                if (!_frozenForEnqueues)
                {
                    _frozenForEnqueues = true;
                    Interlocked.Add(ref _headAndTail.Tail, FreezeOffset);
                }
            }

            public bool TryRemoveItem(T item)
            {
                Slot[] slots = _slots;
                int head = Volatile.Read(ref _headAndTail.Head);
                int tail = Volatile.Read(ref _headAndTail.Tail);

                for (int i = tail - 1; i >= head; i--)
                {
                    int slotsIndex = i & _slotsMask;
                    int currentSeq = Volatile.Read(ref slots[slotsIndex].SequenceNumber);

                    if (currentSeq == i + 1 && (currentSeq & REMOVED_FLAG) == 0)
                    {
                        T? currentItem = slots[slotsIndex].Item;
                        if (EqualityComparer<T>.Default.Equals(currentItem, item))
                        {
                            int removedSeq = currentSeq | REMOVED_FLAG;
                            if (Interlocked.CompareExchange(ref slots[slotsIndex].SequenceNumber, removedSeq, currentSeq) == currentSeq)
                            {
                                slots[slotsIndex].Item = default;
                                return true;
                            }
                        }
                    }
                }

                return false;
            }

            public bool TryDequeue([MaybeNullWhen(false)] out T item)
            {
                Slot[] slots = _slots;

                SpinWait spinner = default;
                while (true)
                {
                    int currentHead = Volatile.Read(ref _headAndTail.Head);
                    int slotsIndex = currentHead & _slotsMask;

                    int sequenceNumber = Volatile.Read(ref slots[slotsIndex].SequenceNumber);

                    if ((sequenceNumber & REMOVED_FLAG) != 0)
                    {
                        if (Interlocked.CompareExchange(ref _headAndTail.Head, currentHead + 1, currentHead) == currentHead)
                        {
                            if (!Volatile.Read(ref _preservedForObservation))
                            {
                                slots[slotsIndex].Item = default;
                                Volatile.Write(ref slots[slotsIndex].SequenceNumber, currentHead + slots.Length);
                            }
                        }
                        continue;
                    }

                    int diff = sequenceNumber - (currentHead + 1);
                    if (diff == 0)
                    {
                        if (Interlocked.CompareExchange(ref _headAndTail.Head, currentHead + 1, currentHead) == currentHead)
                        {
                            item = slots[slotsIndex].Item!;
                            if (!Volatile.Read(ref _preservedForObservation))
                            {
                                slots[slotsIndex].Item = default;
                                Volatile.Write(ref slots[slotsIndex].SequenceNumber, currentHead + slots.Length);
                            }
                            return true;
                        }
                    }
                    else if (diff < 0)
                    {
                        bool frozen = _frozenForEnqueues;
                        int currentTail = Volatile.Read(ref _headAndTail.Tail);
                        if (currentTail - currentHead <= 0 || (frozen && (currentTail - FreezeOffset - currentHead <= 0)))
                        {
                            item = default;
                            return false;
                        }

                        spinner.SpinOnce(sleep1Threshold: -1);
                    }
                }
            }

            /// <summary>
            /// Attempts to return an item from the beginning of the segment without removing it.
            /// </summary>
            /// <param name="result">When this method returns, <paramref name="result"/> contains an item from the beginning of the segment or an unspecified value if the operation failed.</param>
            /// <param name="resultUsed">Indicates whether the result is used.</param>
            /// <returns>true if an item was returned successfully; otherwise, false.</returns>
            public bool TryPeek([MaybeNullWhen(false)] out T result, bool resultUsed)
            {
                if (resultUsed)
                {
                    _preservedForObservation = true;
                    Interlocked.MemoryBarrier();
                }

                Slot[] slots = _slots;

                SpinWait spinner = default;
                while (true)
                {
                    int currentHead = Volatile.Read(ref _headAndTail.Head);
                    int slotsIndex = currentHead & _slotsMask;

                    int sequenceNumber = Volatile.Read(ref slots[slotsIndex].SequenceNumber);

                    int diff = sequenceNumber - (currentHead + 1);
                    if (diff == 0)
                    {
                        result = resultUsed ? slots[slotsIndex].Item! : default!;
                        return true;
                    }
                    else if (diff < 0)
                    {
                        bool frozen = _frozenForEnqueues;
                        int currentTail = Volatile.Read(ref _headAndTail.Tail);
                        if (currentTail - currentHead <= 0 || (frozen && (currentTail - FreezeOffset - currentHead <= 0)))
                        {
                            result = default;
                            return false;
                        }

                        spinner.SpinOnce(sleep1Threshold: -1);
                    }
                }
            }

            /// <summary>
            /// Attempts to add an item to the end of the segment.
            /// </summary>
            /// <param name="item">The item to add to the segment.</param>
            /// <returns>true if the item was added successfully; otherwise, false.</returns>
            public bool TryEnqueue(T item)
            {
                Slot[] slots = _slots;

                while (true)
                {
                    int currentTail = Volatile.Read(ref _headAndTail.Tail);
                    int slotsIndex = currentTail & _slotsMask;

                    int sequenceNumber = Volatile.Read(ref slots[slotsIndex].SequenceNumber);

                    int diff = sequenceNumber - currentTail;
                    if (diff == 0)
                    {
                        if (Interlocked.CompareExchange(ref _headAndTail.Tail, currentTail + 1, currentTail) == currentTail)
                        {
                            slots[slotsIndex].Item = item;
                            Volatile.Write(ref slots[slotsIndex].SequenceNumber, currentTail + 1);
                            return true;
                        }
                    }
                    else if (diff < 0)
                    {
                        return false;
                    }
                }
            }

            /// <summary>
            /// Represents a slot in the <see cref="ConcurrentQueueSegment"/>.
            /// </summary>
            [DebuggerDisplay("Item = {Item}, SequenceNumber = {SequenceNumber}")]
            internal struct Slot
            {
                public T? Item;
                public int SequenceNumber;
            }
        }

        /// <summary>
        /// Represents the head and tail indices of a <see cref="ConcurrentQueueSegment"/>.
        /// </summary>
        [DebuggerDisplay("Head = {Head}, Tail = {Tail}")]
        internal struct PaddedHeadAndTail
        {
            public int Head;
            public int Tail;
        }
    }
}