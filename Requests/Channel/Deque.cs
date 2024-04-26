namespace Requests.Channel
{
    // Licensed to the .NET Foundation under one or more agreements.
    // The .NET Foundation licenses this file to you under the MIT license

    /// <summary>
    /// Represents a double-ended queue (deque) data structure.
    /// </summary>
    /// <typeparam name="T">Specifies the type of elements in the deque.</typeparam>
    internal class Deque<T>
    {
        /// <summary>
        /// The array to hold the elements of the deque.
        /// </summary>
        private T[] _array = Array.Empty<T>();

        /// <summary>
        /// The position of the first valid element in the deque.
        /// </summary>
        private int _head;

        /// <summary>
        /// The position of the first open slot in the deque, unless the deque is full.
        /// </summary>
        private int _tail;

        /// <summary>
        /// The number of elements in the deque.
        /// </summary>
        private int _size;

        /// <summary>
        /// Gets the number of elements contained in the <see cref="Deque{T}"/>.
        /// </summary>
        public int Count => _size;

        /// <summary>
        /// Gets a value indicating whether the <see cref="Deque{T}"/> is empty.
        /// </summary>
        public bool IsEmpty => _size == 0;

        /// <summary>
        /// Adds an item to the tail of the <see cref="Deque{T}"/>.
        /// </summary>
        /// <param name="item">The object to add to the <see cref="Deque{T}"/>.</param>
        public void EnqueueTail(T item)
        {
            if (_size == _array.Length)
                Grow();

            _array[_tail] = item;
            if (++_tail == _array.Length)
                _tail = 0;
            _size++;
        }

        /// <summary>
        /// Removes and returns the object at the head of the <see cref="Deque{T}"/>.
        /// </summary>
        /// <returns>The object that is removed from the head of the <see cref="Deque{T}"/>.</returns>
        public T DequeueHead()
        {
            T item = _array[_head];
            _array[_head] = default!;

            if (++_head == _array.Length)
                _head = 0;
            _size--;

            return item;
        }

        /// <summary>
        /// Returns the object at the head of the <see cref="Deque{T}"/> without removing it.
        /// </summary>
        /// <returns>The object at the head of the <see cref="Deque{T}"/>.</returns>
        public T PeekHead() => _array[_head];

        /// <summary>
        /// Returns the object at the tail of the <see cref="Deque{T}"/> without removing it.
        /// </summary>
        /// <returns>The object at the tail of the <see cref="Deque{T}"/>.</returns>
        public T PeekTail()
        {
            int index = _tail - 1;
            if (index == -1)
                index = _array.Length - 1;
            return _array[index];
        }

        /// <summary>
        /// Removes and returns the object at the tail of the <see cref="Deque{T}"/>.
        /// </summary>
        /// <returns>The object that is removed from the tail of the <see cref="Deque{T}"/>.</returns>
        public T DequeueTail()
        {
            if (--_tail == -1)
                _tail = _array.Length - 1;

            T item = _array[_tail];
            _array[_tail] = default!;

            _size--;
            return item;
        }

        /// <summary>
        /// Returns an enumerator that iterates through the <see cref="Deque{T}"/>.
        /// </summary>
        /// <returns>An enumerator for the <see cref="Deque{T}"/>.</returns>
        public IEnumerator<T> GetEnumerator()
        {
            int pos = _head;
            int count = _size;
            while (count-- > 0)
            {
                yield return _array[pos];
                pos = (pos + 1) % _array.Length;
            }
        }

        /// <summary>
        /// Increases the capacity of the <see cref="Deque{T}"/> to accommodate additional elements.
        /// </summary>
        private void Grow()
        {
            const int MinimumGrow = 4;
            int capacity = (int)(_array.Length * 2L);
            if (capacity < _array.Length + MinimumGrow)
                capacity = _array.Length + MinimumGrow;

            T[] newArray = new T[capacity];

            if (_head == 0)
                Array.Copy(_array, newArray, _size);
            else
            {
                Array.Copy(_array, _head, newArray, 0, _array.Length - _head);
                Array.Copy(_array, 0, newArray, _array.Length - _head, _tail);
            }

            _array = newArray;
            _head = 0;
            _tail = _size;
        }
    }
}
