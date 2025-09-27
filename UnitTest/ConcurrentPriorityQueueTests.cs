using Requests.Channel;
using System.Collections.Concurrent;

namespace UnitTest
{
    /// <summary>
    /// Comprehensive test suite for the ConcurrentPriorityQueue implementation.
    /// Tests functionality, performance, thread safety, and edge cases using NUnit and FluentAssertions.
    /// </summary>
    [TestFixture]
    public class ConcurrentPriorityQueueTests
    {
        private ConcurrentPriorityQueue<string> _queue = null!;

        [SetUp]
        public void SetUp()
        {
            _queue = new ConcurrentPriorityQueue<string>();
        }

        [TearDown]
        public void TearDown()
        {
            _queue?.Clear();
        }

        #region Constructor Tests

        [Test]
        public void Constructor_WithValidCapacity_ShouldInitializeCorrectly()
        {
            // Arrange & Act
            ConcurrentPriorityQueue<string> queue = new(100);

            // Assert
            queue.Count.Should().Be(0);
            queue.IsEmpty.Should().BeTrue();
            queue.Capacity.Should().Be(100);
        }

        [Test]
        public void Constructor_WithDefaultCapacity_ShouldInitializeCorrectly()
        {
            // Arrange & Act
            ConcurrentPriorityQueue<string> queue = new();

            // Assert
            queue.Count.Should().Be(0);
            queue.IsEmpty.Should().BeTrue();
            queue.Capacity.Should().BeGreaterThanOrEqualTo(32);
        }

        [Test]
        public void Constructor_WithInvalidCapacity_ShouldThrowArgumentException()
        {
            // Act & Assert
            Action act = () => new ConcurrentPriorityQueue<string>(-1);
            act.Should().Throw<ArgumentException>()
                .WithMessage("*Queue size must be at least 1*");
        }

        [Test]
        public void Constructor_WithZeroCapacity_ShouldThrowArgumentException()
        {
            // Act & Assert
            Action act = () => new ConcurrentPriorityQueue<string>(0);
            act.Should().Throw<ArgumentException>();
        }

        #endregion

        #region Basic Functionality Tests

        [Test]
        public void Enqueue_SingleItem_ShouldAddCorrectly()
        {
            // Arrange
            PriorityItem<string> item = new(1.0f, "test");

            // Act
            _queue.Enqueue(item);

            // Assert
            _queue.Count.Should().Be(1);
            _queue.IsEmpty.Should().BeFalse();
            _queue.Contains(item).Should().BeTrue();
        }

        [Test]
        public void Enqueue_NullItem_ShouldThrowArgumentNullException()
        {
            // Act & Assert
            Action act = () => _queue.Enqueue(null!);
            act.Should().Throw<ArgumentNullException>();
        }

        [Test]
        public void Dequeue_WithItems_ShouldReturnHighestPriority()
        {
            // Arrange
            PriorityItem<string> highPriority = new(1.0f, "high");
            PriorityItem<string> lowPriority = new(5.0f, "low");
            PriorityItem<string> mediumPriority = new(3.0f, "medium");

            _queue.Enqueue(lowPriority);
            _queue.Enqueue(highPriority);
            _queue.Enqueue(mediumPriority);

            // Act
            PriorityItem<string> result = _queue.Dequeue();

            // Assert
            result.Should().Be(highPriority);
            _queue.Count.Should().Be(2);
        }

        [Test]
        public void Dequeue_EmptyQueue_ShouldThrowInvalidOperationException()
        {
            // Act & Assert
            Action act = () => _queue.Dequeue();
            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*Cannot dequeue from an empty queue*");
        }

        [Test]
        public void TryDequeue_WithItems_ShouldReturnTrueAndItem()
        {
            // Arrange
            PriorityItem<string> item = new(1.0f, "test");
            _queue.Enqueue(item);

            // Act
            bool result = _queue.TryDequeue(out PriorityItem<string>? dequeuedItem);

            // Assert
            result.Should().BeTrue();
            dequeuedItem.Should().Be(item);
            _queue.Count.Should().Be(0);
        }

        [Test]
        public void TryDequeue_EmptyQueue_ShouldReturnFalseAndNull()
        {
            // Act
            bool result = _queue.TryDequeue(out PriorityItem<string>? item);

            // Assert
            result.Should().BeFalse();
            item.Should().BeNull();
        }

        [Test]
        public void Peek_WithItems_ShouldReturnHighestPriorityWithoutRemoving()
        {
            // Arrange
            PriorityItem<string> highPriority = new(1.0f, "high");
            PriorityItem<string> lowPriority = new(5.0f, "low");

            _queue.Enqueue(lowPriority);
            _queue.Enqueue(highPriority);

            // Act
            PriorityItem<string> result = _queue.Peek();

            // Assert
            result.Should().Be(highPriority);
            _queue.Count.Should().Be(2);
        }

        [Test]
        public void Peek_EmptyQueue_ShouldThrowInvalidOperationException()
        {
            // Act & Assert
            Action act = () => _queue.Peek();
            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*Cannot peek at an empty queue*");
        }

        [Test]
        public void TryPeek_WithItems_ShouldReturnTrueAndItem()
        {
            // Arrange
            PriorityItem<string> item = new(1.0f, "test");
            _queue.Enqueue(item);

            // Act
            bool result = _queue.TryPeek(out PriorityItem<string>? peekedItem);

            // Assert
            result.Should().BeTrue();
            peekedItem.Should().Be(item);
            _queue.Count.Should().Be(1);
        }

        [Test]
        public void TryPeek_EmptyQueue_ShouldReturnFalseAndNull()
        {
            // Act
            bool result = _queue.TryPeek(out PriorityItem<string>? item);

            // Assert
            result.Should().BeFalse();
            item.Should().BeNull();
        }

        #endregion

        #region Priority Ordering Tests

        [Test]
        public void PriorityOrdering_MultipleItems_ShouldMaintainCorrectOrder()
        {
            // Arrange
            PriorityItem<int>[] items = new[]
            {
                new PriorityItem<int>(5.0f, 5),
                new PriorityItem<int>(1.0f, 1),
                new PriorityItem<int>(3.0f, 3),
                new PriorityItem<int>(2.0f, 2),
                new PriorityItem<int>(4.0f, 4)
            };
            ConcurrentPriorityQueue<int> queue = new();

            // Act
            foreach (PriorityItem<int>? item in items)
                queue.Enqueue(item);

            List<int> results = new();
            while (!queue.IsEmpty)
                results.Add(queue.Dequeue().Item);

            // Assert
            results.Should().Equal(1, 2, 3, 4, 5);
        }

        [Test]
        public void StablePriorityOrdering_SamePriorityItems_ShouldMaintainFIFOOrder()
        {
            // Arrange
            PriorityItem<string>[] items = new[]
            {
                new PriorityItem<string>(2.0f, "first"),
                new PriorityItem<string>(2.0f, "second"),
                new PriorityItem<string>(2.0f, "third"),
                new PriorityItem<string>(1.0f, "highest"),
                new PriorityItem<string>(2.0f, "fourth")
            };
            ConcurrentPriorityQueue<string> queue = new();

            // Act
            foreach (PriorityItem<string>? item in items)
                queue.Enqueue(item);

            List<string> results = new();
            while (!queue.IsEmpty)
                results.Add(queue.Dequeue().Item);

            // Assert
            results.Should().Equal("highest", "first", "second", "third", "fourth");
        }

        [Test]
        public void PriorityOrdering_FloatPrecision_ShouldHandleCorrectly()
        {
            // Arrange
            ConcurrentPriorityQueue<string> queue = new();
            queue.Enqueue(new PriorityItem<string>(1.1f, "a"));
            queue.Enqueue(new PriorityItem<string>(1.01f, "b"));
            queue.Enqueue(new PriorityItem<string>(1.001f, "c"));

            // Act & Assert
            queue.Dequeue().Item.Should().Be("c");
            queue.Dequeue().Item.Should().Be("b");
            queue.Dequeue().Item.Should().Be("a");
        }

        #endregion

        #region Thread Safety Tests

        [Test]
        public void ConcurrentEnqueue_MultipleThreads_ShouldAddAllItems()
        {
            // Arrange
            const int threadCount = 10;
            const int itemsPerThread = 100;
            Barrier barrier = new(threadCount);
            Task[] tasks = new Task[threadCount];
            ConcurrentPriorityQueue<int> queue = new();

            // Act
            for (int t = 0; t < threadCount; t++)
            {
                int threadId = t;
                tasks[t] = Task.Run(() =>
                {
                    barrier.SignalAndWait();
                    for (int i = 0; i < itemsPerThread; i++)
                    {
                        PriorityItem<int> item = new(Random.Shared.NextSingle() * 100, threadId * itemsPerThread + i);
                        queue.Enqueue(item);
                    }
                });
            }

            Task.WaitAll(tasks);

            // Assert
            queue.Count.Should().Be(threadCount * itemsPerThread);
            queue.IsValidQueue().Should().BeTrue();
        }

        [Test]
        public void ConcurrentDequeue_MultipleThreads_ShouldRemoveAllItems()
        {
            // Arrange
            const int itemCount = 1000;
            ConcurrentBag<int> dequeueResults = new();
            ConcurrentPriorityQueue<int> queue = new();

            for (int i = 0; i < itemCount; i++)
            {
                queue.Enqueue(new PriorityItem<int>(Random.Shared.NextSingle() * 100, i));
            }

            // Act
            Task[] tasks = Enumerable.Range(0, 10).Select(_ => Task.Run(() =>
            {
                while (queue.TryDequeue(out PriorityItem<int>? item))
                {
                    dequeueResults.Add(item.Item);
                    Thread.Sleep(1);
                }
            })).ToArray();

            Task.WaitAll(tasks);

            // Assert
            queue.Count.Should().Be(0);
            dequeueResults.Should().HaveCount(itemCount);
            queue.IsValidQueue().Should().BeTrue();
        }

        [Test]
        public void ConcurrentMixedOperations_MultipleThreads_ShouldMaintainConsistency()
        {
            // Arrange
            const int operationsPerThread = 50;
            ConcurrentPriorityQueue<int> queue = new();
            Task[] tasks = new Task[4];
            ConcurrentBag<Exception> exceptions = new();
            using CancellationTokenSource cts = new(TimeSpan.FromSeconds(10)); // 10 second timeout

            // Act
            tasks[0] = Task.Run(() => // Enqueue thread
            {
                try
                {
                    for (int i = 0; i < operationsPerThread && !cts.Token.IsCancellationRequested; i++)
                    {
                        queue.Enqueue(new PriorityItem<int>(Random.Shared.NextSingle() * 100, i));
                        Thread.Sleep(1);
                    }
                }
                catch (Exception ex) { exceptions.Add(ex); }
            }, cts.Token);

            tasks[1] = Task.Run(() => // Dequeue thread
            {
                try
                {
                    int dequeued = 0;
                    while (dequeued < operationsPerThread / 2 && !cts.Token.IsCancellationRequested)
                    {
                        if (queue.TryDequeue(out _))
                            dequeued++;
                        Thread.Sleep(1);
                    }
                }
                catch (Exception ex) { exceptions.Add(ex); }
            }, cts.Token);

            tasks[2] = Task.Run(() => // Peek thread
            {
                try
                {
                    for (int i = 0; i < operationsPerThread && !cts.Token.IsCancellationRequested; i++)
                    {
                        queue.TryPeek(out _);
                        Thread.Sleep(1);
                    }
                }
                catch (Exception ex) { exceptions.Add(ex); }
            }, cts.Token);

            tasks[3] = Task.Run(() => // Count monitoring thread
            {
                try
                {
                    for (int i = 0; i < operationsPerThread && !cts.Token.IsCancellationRequested; i++)
                    {
                        int count = queue.Count;
                        count.Should().BeGreaterThanOrEqualTo(0);
                        Thread.Sleep(1);
                    }
                }
                catch (Exception ex) { exceptions.Add(ex); }
            }, cts.Token);

            bool completed = Task.WaitAll(tasks, TimeSpan.FromSeconds(15)); // 15 second overall timeout

            // Assert
            completed.Should().BeTrue("All tasks should complete within timeout");
            exceptions.Should().BeEmpty();
            queue.IsValidQueue().Should().BeTrue();
        }

        #endregion

        #region Performance Tests

        [Test]
        [Category("Performance")]
        public void Performance_LargeNumberOfItems_ShouldCompleteInReasonableTime()
        {
            // Arrange
            const int itemCount = 50000;
            ConcurrentPriorityQueue<int> queue = new();
            Stopwatch stopwatch = new();

            // Act - Enqueue
            stopwatch.Start();
            for (int i = 0; i < itemCount; i++)
            {
                queue.Enqueue(new PriorityItem<int>(Random.Shared.NextSingle() * 1000, i));
            }
            stopwatch.Stop();
            long enqueueTime = stopwatch.ElapsedMilliseconds;

            // Act - Dequeue
            stopwatch.Restart();
            while (!queue.IsEmpty)
            {
                queue.Dequeue();
            }
            stopwatch.Stop();
            long dequeueTime = stopwatch.ElapsedMilliseconds;

            // Assert
            enqueueTime.Should().BeLessThan(3000, "Enqueue operations should be efficient");
            dequeueTime.Should().BeLessThan(3000, "Dequeue operations should be efficient");
        }

        [Test]
        [Category("Performance")]
        public void Performance_ComparedToNaiveApproach_ShouldShowImprovement()
        {
            // Arrange
            const int itemCount = 5000;
            List<PriorityItem<int>> items = Enumerable.Range(0, itemCount)
                .Select(i => new PriorityItem<int>(Random.Shared.NextSingle() * 1000, i))
                .ToList();

            // Test optimized queue
            ConcurrentPriorityQueue<int> optimizedQueue = new();
            Stopwatch stopwatch = Stopwatch.StartNew();

            foreach (PriorityItem<int>? item in items)
                optimizedQueue.Enqueue(item);

            while (!optimizedQueue.IsEmpty)
                optimizedQueue.Dequeue();

            stopwatch.Stop();
            long optimizedTime = stopwatch.ElapsedMilliseconds;

            // Test naive approach
            List<PriorityItem<int>> naiveList = new();
            stopwatch.Restart();

            foreach (PriorityItem<int>? item in items)
            {
                naiveList.Add(item);
                naiveList.Sort((a, b) => a.Priority.CompareTo(b.Priority));
            }

            while (naiveList.Count > 0)
            {
                naiveList.RemoveAt(0);
                if (naiveList.Count > 0)
                    naiveList.Sort((a, b) => a.Priority.CompareTo(b.Priority));
            }

            stopwatch.Stop();
            long naiveTime = stopwatch.ElapsedMilliseconds;

            // Assert
            optimizedTime.Should().BeLessThan(naiveTime,
                $"Optimized approach ({optimizedTime}ms) should be faster than naive approach ({naiveTime}ms)");
        }

        #endregion

        #region Remove and Contains Tests

        [Test]
        public void Contains_ExistingItem_ShouldReturnTrue()
        {
            // Arrange
            PriorityItem<string> item = new(1.0f, "test");
            _queue.Enqueue(item);

            // Act & Assert
            _queue.Contains(item).Should().BeTrue();
        }

        [Test]
        public void Contains_NonExistentItem_ShouldReturnFalse()
        {
            // Arrange
            PriorityItem<string> item1 = new(1.0f, "test1");
            PriorityItem<string> item2 = new(2.0f, "test2");
            _queue.Enqueue(item1);

            // Act & Assert
            _queue.Contains(item2).Should().BeFalse();
        }

        [Test]
        public void Contains_NullItem_ShouldThrowArgumentNullException()
        {
            // Act & Assert
            Action act = () => _queue.Contains(null!);
            act.Should().Throw<ArgumentNullException>();
        }

        [Test]
        public void TryRemove_ExistingItem_ShouldRemoveAndReturnTrue()
        {
            // Arrange
            PriorityItem<string> item1 = new(1.0f, "first");
            PriorityItem<string> item2 = new(2.0f, "second");
            PriorityItem<string> item3 = new(3.0f, "third");

            _queue.Enqueue(item1);
            _queue.Enqueue(item2);
            _queue.Enqueue(item3);

            // Act
            bool result = _queue.TryRemove(item2);

            // Assert
            result.Should().BeTrue();
            _queue.Count.Should().Be(2);
            _queue.Contains(item2).Should().BeFalse();
            _queue.IsValidQueue().Should().BeTrue();
        }

        [Test]
        public void TryRemove_NonExistentItem_ShouldReturnFalse()
        {
            // Arrange
            PriorityItem<string> item1 = new(1.0f, "first");
            PriorityItem<string> item2 = new(2.0f, "second");
            _queue.Enqueue(item1);

            // Act
            bool result = _queue.TryRemove(item2);

            // Assert
            result.Should().BeFalse();
            _queue.Count.Should().Be(1);
        }

        [Test]
        public void TryRemove_NullItem_ShouldThrowArgumentNullException()
        {
            // Act & Assert
            Action act = () => _queue.TryRemove(null!);
            act.Should().Throw<ArgumentNullException>();
        }

        [Test]
        public void TryRemove_RootItem_ShouldMaintainHeapProperty()
        {
            // Arrange
            PriorityItem<int>[] items = Enumerable.Range(1, 10)
                .Select(i => new PriorityItem<int>(i, i))
                .ToArray();
            ConcurrentPriorityQueue<int> queue = new();

            foreach (PriorityItem<int>? item in items)
                queue.Enqueue(item);

            // Act - Remove the root (highest priority)
            bool result = queue.TryRemove(items[0]); // Priority 1

            // Assert
            result.Should().BeTrue();
            queue.Count.Should().Be(9);
            queue.IsValidQueue().Should().BeTrue();
            queue.Peek().Item.Should().Be(2); // Next highest priority
        }

        #endregion

        #region Clear and Utility Tests

        [Test]
        public void Clear_PopulatedQueue_ShouldRemoveAllItems()
        {
            // Arrange
            for (int i = 0; i < 100; i++)
            {
                _queue.Enqueue(new PriorityItem<string>(i, $"item{i}"));
            }

            // Act
            _queue.Clear();

            // Assert
            _queue.Count.Should().Be(0);
            _queue.IsEmpty.Should().BeTrue();
            _queue.IsValidQueue().Should().BeTrue();
        }

        [Test]
        public void ToArray_PopulatedQueue_ShouldReturnSortedArray()
        {
            // Arrange
            float[] priorities = new[] { 5.0f, 1.0f, 3.0f, 2.0f, 4.0f };
            ConcurrentPriorityQueue<int> queue = new();

            for (int i = 0; i < priorities.Length; i++)
            {
                queue.Enqueue(new PriorityItem<int>(priorities[i], i));
            }

            // Act
            PriorityItem<int>[] array = queue.ToArray();

            // Assert
            array.Should().HaveCount(5);

            for (int i = 1; i < array.Length; i++)
            {
                array[i - 1].Priority.Should().BeLessThanOrEqualTo(array[i].Priority);
            }
        }

        [Test]
        public void ToArray_EmptyQueue_ShouldReturnEmptyArray()
        {
            // Act
            PriorityItem<string>[] array = _queue.ToArray();

            // Assert
            array.Should().BeEmpty();
        }

        [Test]
        public void IsValidQueue_AlwaysTrue_ForProperlyMaintainedHeap()
        {
            // Arrange
            ConcurrentPriorityQueue<int> queue = new();
            Random random = new(42);

            // Act & Assert
            queue.IsValidQueue().Should().BeTrue("Empty queue should be valid");

            for (int i = 0; i < 500; i++)
            {
                queue.Enqueue(new PriorityItem<int>(random.NextSingle() * 100, i));
                queue.IsValidQueue().Should().BeTrue($"Heap property should be maintained after enqueue {i}");
            }

            for (int i = 0; i < 250; i++)
            {
                queue.Dequeue();
                queue.IsValidQueue().Should().BeTrue($"Heap property should be maintained after dequeue {i}");
            }
        }

        #endregion

        #region Enumeration Tests

        [Test]
        public void GetEnumerator_PopulatedQueue_ShouldReturnAllItems()
        {
            // Arrange
            PriorityItem<string>[] items = new[]
            {
                new PriorityItem<string>(1.0f, "first"),
                new PriorityItem<string>(2.0f, "second"),
                new PriorityItem<string>(3.0f, "third")
            };

            foreach (PriorityItem<string>? item in items)
                _queue.Enqueue(item);

            // Act
            List<PriorityItem<string>> enumeratedItems = _queue.ToList();

            // Assert
            enumeratedItems.Should().HaveCount(3);
            foreach (PriorityItem<string>? item in items)
            {
                enumeratedItems.Should().Contain(item);
            }
        }

        [Test]
        public void GetEnumerator_EmptyQueue_ShouldReturnNoItems()
        {
            // Act
            List<PriorityItem<string>> enumeratedItems = _queue.ToList();

            // Assert
            enumeratedItems.Should().BeEmpty();
        }

        [Test]
        public void GetEnumerator_ConcurrentModification_ShouldReflectLiveState()
        {
            // Arrange
            PriorityItem<string> item1 = new(1.0f, "test");
            _queue.Enqueue(item1);

            // Act
            IEnumerator<PriorityItem<string>> enumerator = _queue.GetEnumerator();
            PriorityItem<string> item2 = new(2.0f, "test2");
            _queue.Enqueue(item2);
            List<PriorityItem<string>> items = new();

            while (enumerator.MoveNext())
            {
                items.Add(enumerator.Current);
            }

            // Asser
            items.Should().HaveCount(2);
            items.Should().Contain(item1);
            items.Should().Contain(item2);
        }

        #endregion

        #region TryEnqueue Tests

        [Test]
        public void TryEnqueue_ValidItem_ShouldReturnTrueAndAddItem()
        {
            // Arrange
            PriorityItem<string> item = new(1.0f, "test");

            // Act
            bool result = _queue.TryEnqueue(item);

            // Assert
            result.Should().BeTrue();
            _queue.Count.Should().Be(1);
            _queue.Contains(item).Should().BeTrue();
        }

        [Test]
        public void TryEnqueue_NullItem_ShouldReturnFalse()
        {
            // Act
            bool result = _queue.TryEnqueue(null!);

            // Assert
            result.Should().BeFalse();
            _queue.Count.Should().Be(0);
        }

        #endregion
    }
}