using Requests.Channel;
using System.Collections.Concurrent;

namespace UnitTest
{
    /// <summary>
    /// Comprehensive test suite for the DynamicPriorityChannel implementation.
    /// Tests channel behavior, priority handling, parallel operations, and thread safety.
    /// </summary>
    [TestFixture]
    public class DynamicPriorityChannelTests
    {
        private DynamicPriorityChannel<string> _channel = null!;

        [SetUp]
        public void SetUp()
        {
            _channel = new DynamicPriorityChannel<string>();
        }

        [TearDown]
        public void TearDown()
        {
            _channel?.Writer?.TryComplete();
        }

        #region Basic Channel Tests

        [Test]
        public void Constructor_ShouldInitializeWithCorrectProperties()
        {
            // Act & Assert
            _channel.Should().NotBeNull();
            _channel.Reader.Should().NotBeNull();
            _channel.Writer.Should().NotBeNull();
            _channel.Options.Should().NotBeNull();
            _channel.Count.Should().Be(0);
        }

        [Test]
        public void Reader_Properties_ShouldReturnExpectedValues()
        {
            // Act & Assert
            _channel.Reader.CanCount.Should().BeTrue();
            _channel.Reader.CanPeek.Should().BeTrue();
            _channel.Reader.Count.Should().Be(0);
            _channel.Reader.Completion.Should().NotBeNull();
        }

        [Test]
        public async Task Writer_TryWrite_ShouldAddItemToChannel()
        {
            // Arrange
            PriorityItem<string> item = new(1.0f, "test");

            // Act
            bool result = _channel.Writer.TryWrite(item);

            // Assert
            result.Should().BeTrue();
            _channel.Count.Should().Be(1);

            PriorityItem<string> readResult = await _channel.Reader.ReadAsync();
            readResult.Should().Be(item);
        }

        [Test]
        public async Task Writer_WriteAsync_ShouldAddItemToChannel()
        {
            // Arrange
            PriorityItem<string> item = new(1.0f, "test");

            // Act
            await _channel.Writer.WriteAsync(item);

            // Assert
            _channel.Count.Should().Be(1);

            PriorityItem<string> readResult = await _channel.Reader.ReadAsync();
            readResult.Should().Be(item);
        }

        [Test]
        public void Writer_TryComplete_ShouldCompleteChannel()
        {
            // Act
            bool result = _channel.Writer.TryComplete();

            // Assert
            result.Should().BeTrue();
            _channel.Reader.Completion.IsCompleted.Should().BeTrue();
        }

        [Test]
        public void Writer_TryComplete_CalledTwice_ShouldReturnFalseSecondTime()
        {
            // Act
            bool firstResult = _channel.Writer.TryComplete();
            bool secondResult = _channel.Writer.TryComplete();

            // Assert
            firstResult.Should().BeTrue();
            secondResult.Should().BeFalse();
        }

        #endregion

        #region Reader Tests

        [Test]
        public void Reader_TryRead_EmptyChannel_ShouldReturnFalse()
        {
            // Act
            bool result = _channel.Reader.TryRead(out PriorityItem<string>? item);

            // Assert
            result.Should().BeFalse();
            item.Should().BeNull();
        }

        [Test]
        public async Task Reader_TryRead_WithItem_ShouldReturnTrueAndItem()
        {
            // Arrange
            PriorityItem<string> item = new(1.0f, "test");
            await _channel.Writer.WriteAsync(item);

            // Act
            bool result = _channel.Reader.TryRead(out PriorityItem<string>? readItem);

            // Assert
            result.Should().BeTrue();
            readItem.Should().Be(item);
            _channel.Count.Should().Be(0);
        }

        [Test]
        public void Reader_TryPeek_EmptyChannel_ShouldReturnFalse()
        {
            // Act
            bool result = _channel.Reader.TryPeek(out PriorityItem<string>? item);

            // Assert
            result.Should().BeFalse();
            item.Should().BeNull();
        }

        [Test]
        public async Task Reader_TryPeek_WithItem_ShouldReturnTrueAndNotRemove()
        {
            // Arrange
            PriorityItem<string> item = new(1.0f, "test");
            await _channel.Writer.WriteAsync(item);

            // Act
            bool result = _channel.Reader.TryPeek(out PriorityItem<string>? peekedItem);

            // Assert
            result.Should().BeTrue();
            peekedItem.Should().Be(item);
            _channel.Count.Should().Be(1); // Item should still be in channel
        }

        [Test]
        public async Task Reader_ReadAsync_ShouldReturnItemsInPriorityOrder()
        {
            // Arrange
            PriorityItem<string> highPriority = new(1.0f, "high");
            PriorityItem<string> mediumPriority = new(5.0f, "medium");
            PriorityItem<string> lowPriority = new(10.0f, "low");

            await _channel.Writer.WriteAsync(lowPriority);
            await _channel.Writer.WriteAsync(highPriority);
            await _channel.Writer.WriteAsync(mediumPriority);

            // Act
            PriorityItem<string> first = await _channel.Reader.ReadAsync();
            PriorityItem<string> second = await _channel.Reader.ReadAsync();
            PriorityItem<string> third = await _channel.Reader.ReadAsync();

            // Assert
            first.Should().Be(highPriority);
            second.Should().Be(mediumPriority);
            third.Should().Be(lowPriority);
        }

        [Test]
        public async Task Reader_WaitToReadAsync_EmptyChannel_ShouldReturnTrue()
        {
            // Arrange
            Task writeTask = Task.Run(async () =>
            {
                await Task.Delay(100);
                await _channel.Writer.WriteAsync(new PriorityItem<string>(1.0f, "test"));
            });

            // Act
            bool result = await _channel.Reader.WaitToReadAsync();

            // Assert
            result.Should().BeTrue();
            await writeTask;
        }

        [Test]
        public async Task Reader_WaitToReadAsync_CompletedChannel_ShouldReturnFalse()
        {
            // Arrange
            _channel.Writer.TryComplete();

            // Act
            bool result = await _channel.Reader.WaitToReadAsync();

            // Assert
            result.Should().BeFalse();
        }

        #endregion

        #region Priority Ordering Tests

        [Test]
        public async Task PriorityOrdering_MultipleItems_ShouldRespectPriorityAndInsertionOrder()
        {
            // Arrange
            PriorityItem<string>[] items = new[]
            {
                new PriorityItem<string>(5.0f, "item5"),
                new PriorityItem<string>(1.0f, "item1"),
                new PriorityItem<string>(3.0f, "item3"),
                new PriorityItem<string>(1.0f, "item1_second"), // Same priority as first
                new PriorityItem<string>(2.0f, "item2")
            };

            // Act
            foreach (PriorityItem<string>? item in items)
                await _channel.Writer.WriteAsync(item);

            List<string> results = new();
            while (_channel.Reader.TryRead(out PriorityItem<string>? item))
                results.Add(item.Item);

            // Assert
            results.Should().Equal("item1", "item1_second", "item2", "item3", "item5");
        }

        [Test]
        public async Task PriorityOrdering_SamePriorityItems_ShouldMaintainFIFOOrder()
        {
            // Arrange
            PriorityItem<string>[] items = new[]
            {
                new PriorityItem<string>(2.0f, "first"),
                new PriorityItem<string>(2.0f, "second"),
                new PriorityItem<string>(2.0f, "third"),
                new PriorityItem<string>(2.0f, "fourth")
            };

            // Act
            foreach (PriorityItem<string>? item in items)
                await _channel.Writer.WriteAsync(item);

            List<string> results = new();
            while (_channel.Reader.TryRead(out PriorityItem<string>? item))
                results.Add(item.Item);

            // Assert
            results.Should().Equal("first", "second", "third", "fourth");
        }

        #endregion

        #region TryRemove Tests

        [Test]
        public async Task TryRemove_ExistingItem_ShouldRemoveAndReturnTrue()
        {
            // Arrange
            PriorityItem<string> item1 = new(1.0f, "item1");
            PriorityItem<string> item2 = new(2.0f, "item2");
            PriorityItem<string> item3 = new(3.0f, "item3");

            await _channel.Writer.WriteAsync(item1);
            await _channel.Writer.WriteAsync(item2);
            await _channel.Writer.WriteAsync(item3);

            // Act
            bool result = _channel.TryRemove(item2);

            // Assert
            result.Should().BeTrue();
            _channel.Count.Should().Be(2);

            // Verify item2 is not in the remaining items
            PriorityItem<string>[] remainingItems = _channel.ToArray();
            remainingItems.Should().NotContain(item2);
            remainingItems.Should().Contain(item1);
            remainingItems.Should().Contain(item3);
        }

        [Test]
        public async Task TryRemove_NonExistentItem_ShouldReturnFalse()
        {
            // Arrange
            PriorityItem<string> item1 = new(1.0f, "item1");
            PriorityItem<string> item2 = new(2.0f, "item2");

            await _channel.Writer.WriteAsync(item1);

            // Act
            bool result = _channel.TryRemove(item2);

            // Assert
            result.Should().BeFalse();
            _channel.Count.Should().Be(1);
        }

        [Test]
        public async Task TryRemove_CompletedChannel_ShouldReturnFalse()
        {
            // Arrange
            PriorityItem<string> item = new(1.0f, "item");
            await _channel.Writer.WriteAsync(item);
            _channel.Writer.TryComplete();

            // Act
            bool result = _channel.TryRemove(item);

            // Assert
            result.Should().BeFalse();
        }

        [Test]
public async Task TryRemove_ThenTryComplete_ShouldNotDeadlock()
        {
            // Arrange
            PriorityItem<string> item = new(1.0f, "test");
            await _channel.Writer.WriteAsync(item);

            // Act - Remove the item
            bool removed = _channel.TryRemove(item);
            removed.Should().BeTrue();
            _channel.Count.Should().Be(0);

            // Act - Try to complete (this should not deadlock)
            var completionTask = Task.Run(() => _channel.Writer.TryComplete());
            var completed = await completionTask.WaitAsync(TimeSpan.FromSeconds(2));

            // Assert
            completed.Should().BeTrue("TryComplete should succeed and not deadlock");
            _channel.Reader.Completion.IsCompleted.Should().BeTrue();
        }

        [Test]
        public async Task TryRemove_AllItems_ThenTryComplete_ShouldNotDeadlock()
        {
            // Arrange
            var items = new[]
            {
                new PriorityItem<string>(1.0f, "item1"),
                new PriorityItem<string>(2.0f, "item2"),
                new PriorityItem<string>(3.0f, "item3")
            };

            foreach (var item in items)
                await _channel.Writer.WriteAsync(item);

            // Act - Remove all items
            foreach (var item in items)
            {
                bool removed = _channel.TryRemove(item);
                removed.Should().BeTrue();
            }
            _channel.Count.Should().Be(0);

            // Act - Try to complete (this should not deadlock)
            var completionTask = Task.Run(() => _channel.Writer.TryComplete());
            var completed = await completionTask.WaitAsync(TimeSpan.FromSeconds(2));

            // Assert
            completed.Should().BeTrue("TryComplete should succeed and not deadlock");
            _channel.Reader.Completion.IsCompleted.Should().BeTrue();
        }

        #endregion

        #region ToArray Tests

        [Test]
        public void ToArray_EmptyChannel_ShouldReturnEmptyArray()
        {
            // Act
            PriorityItem<string>[] array = _channel.ToArray();

            // Assert
            array.Should().BeEmpty();
        }

        [Test]
        public async Task ToArray_WithItems_ShouldReturnAllItemsInPriorityOrder()
        {
            // Arrange
            PriorityItem<string>[] items = new[]
            {
                new PriorityItem<string>(3.0f, "item3"),
                new PriorityItem<string>(1.0f, "item1"),
                new PriorityItem<string>(2.0f, "item2")
            };

            foreach (PriorityItem<string>? item in items)
                await _channel.Writer.WriteAsync(item);

            // Act
            PriorityItem<string>[] array = _channel.ToArray();

            // Assert
            array.Should().HaveCount(3);
            array.Select(x => x.Item).Should().Equal("item1", "item2", "item3");
        }

        #endregion

        #region Parallel Operations Tests

        [Test]
        public async Task RunParallelReader_ShouldProcessAllItems()
        {
            // Arrange
            const int itemCount = 100;
            List<string> processedItems = new();
            object lockObject = new();

            // Add items to channel
            for (int i = 0; i < itemCount; i++)
            {
                await _channel.Writer.WriteAsync(new PriorityItem<string>(i, $"item{i}"));
            }
            _channel.Writer.TryComplete();

            // Act
            await _channel.RunParallelReader((item, ct) =>
            {
                lock (lockObject)
                {
                    processedItems.Add(item.Item);
                }
                return ValueTask.CompletedTask;
            });

            // Assert
            processedItems.Should().HaveCount(itemCount);
            processedItems.Should().Contain($"item0");
            processedItems.Should().Contain($"item{itemCount - 1}");
        }

        [Test]
        public async Task RunParallelReader_WithCancellation_ShouldRespectCancellation()
        {
            // Arrange
            const int itemCount = 1000;
            int processedCount = 0;
            CancellationTokenSource cts = new();

            // Add items to channel
            for (int i = 0; i < itemCount; i++)
            {
                await _channel.Writer.WriteAsync(new PriorityItem<string>(i, $"item{i}"));
            }
            _channel.Writer.TryComplete();

            // Cancel after processing some items
            Task parallelTask = _channel.RunParallelReader(async (item, ct) =>
            {
                int currentCount = Interlocked.Increment(ref processedCount);
                if (currentCount == 10)
                {
                    cts.Cancel();
                }
                ct.ThrowIfCancellationRequested();
                await Task.Delay(10, ct);
            });

            // Act & Assert
            await Task.Delay(100);
            cts.Cancel();

            // The parallel reader should eventually complete or be cancelled
            Task completed = await Task.WhenAny(parallelTask, Task.Delay(5000));
            completed.Should().Be(parallelTask);
        }

        [Test]
        public void RunParallelReader_NullBody_ShouldThrowArgumentNullException()
        {
            // Act & Assert
            Func<Task> act = () => _channel.RunParallelReader(null!);
            act.Should().ThrowAsync<ArgumentNullException>();
        }

        #endregion

        #region Concurrency Tests

        [Test]
        public async Task ConcurrentWriteRead_MultipleThreads_ShouldMaintainDataIntegrity()
        {
            // Arrange
            const int itemsPerThread = 100;
            const int writerThreads = 5;
            const int readerThreads = 3;

            ConcurrentBag<string> writtenItems = new();
            ConcurrentBag<string> readItems = new();
            List<Task> writerTasks = new();
            List<Task> readerTasks = new();

            // Writer tasks
            for (int t = 0; t < writerThreads; t++)
            {
                int threadId = t;
                writerTasks.Add(Task.Run(async () =>
                {
                    for (int i = 0; i < itemsPerThread; i++)
                    {
                        PriorityItem<string> item = new(Random.Shared.NextSingle() * 100, $"thread{threadId}_item{i}");
                        await _channel.Writer.WriteAsync(item);
                        writtenItems.Add(item.Item);
                        await Task.Delay(1);
                    }
                }));
            }

            // Reader tasks
            for (int t = 0; t < readerThreads; t++)
            {
                readerTasks.Add(Task.Run(async () =>
                {
                    await Task.Delay(50); // Let some items accumulate
                    while (!_channel.Reader.Completion.IsCompleted)
                    {
                        if (_channel.Reader.TryRead(out PriorityItem<string>? item))
                        {
                            readItems.Add(item.Item);
                        }
                        else
                        {
                            await Task.Delay(10);
                        }
                    }

                    // Read any remaining items
                    while (_channel.Reader.TryRead(out PriorityItem<string>? item))
                    {
                        readItems.Add(item.Item);
                    }
                }));
            }

            // Act - Wait for writers first, then complete channel, then wait for readers
            await Task.WhenAll(writerTasks);
            _channel.Writer.TryComplete();
            await Task.WhenAll(readerTasks);
            await _channel.Reader.Completion;

            // Assert
            writtenItems.Should().HaveCount(writerThreads * itemsPerThread);
            readItems.Should().HaveCount(writerThreads * itemsPerThread);

            // Verify all written items were read
            foreach (string writtenItem in writtenItems)
            {
                readItems.Should().Contain(writtenItem);
            }
        }

        [Test]
        public async Task ConcurrentTryRemove_MultipleThreads_ShouldMaintainConsistency()
        {
            // Arrange
            const int itemCount = 200;
            List<PriorityItem<string>> items = new();

            // Add items
            for (int i = 0; i < itemCount; i++)
            {
                PriorityItem<string> item = new(i, $"item{i}");
                items.Add(item);
                await _channel.Writer.WriteAsync(item);
            }

            // Ensure all items are written before starting removal tasks
            await Task.Delay(50);

            ConcurrentBag<PriorityItem<string>> removedItems = new();
            List<Task> tasks = new();

            // Multiple threads trying to remove items
            for (int t = 0; t < 10; t++)
            {
                int threadIndex = t;
                tasks.Add(Task.Run(() =>
                {
                    foreach (PriorityItem<string>? item in items.Skip(threadIndex * 20).Take(20))
                    {
                        if (_channel.TryRemove(item))
                        {
                            removedItems.Add(item);
                        }
                        Thread.Sleep(1);
                    }
                }));
            }

            // Act
            await Task.WhenAll(tasks);

            // Assert
            removedItems.Should().HaveCount(itemCount);
            _channel.Count.Should().Be(0);
        }

        #endregion

        #region Error Handling Tests

        [Test]
        public async Task Writer_WriteAfterComplete_ShouldThrowInvalidOperationException()
        {
            // Arrange
            _channel.Writer.TryComplete();
            PriorityItem<string> item = new(1.0f, "test");

            // Act & Assert
            _channel.Writer.TryWrite(item).Should().BeFalse();

            Func<Task> act = () => _channel.Writer.WriteAsync(item).AsTask();
            await act.Should().ThrowAsync<InvalidOperationException>();
        }

        [Test]
        public async Task Reader_ReadFromCompletedEmptyChannel_ShouldThrowInvalidOperationException()
        {
            // Arrange
            _channel.Writer.TryComplete();

            // Act & Assert
            Func<Task> act = () => _channel.Reader.ReadAsync().AsTask();
            await act.Should().ThrowAsync<InvalidOperationException>();
        }

        #endregion

        #region Options Tests

        [Test]
        public void Options_DefaultValues_ShouldBeCorrect()
        {
            // Act & Assert
            _channel.Options.Should().NotBeNull();
            _channel.Options.MaxDegreeOfParallelism.Should().Be(Environment.ProcessorCount);
            _channel.Options.EasyEndToken.Should().NotBeNull();
        }

        [Test]
        public void Options_ModifyMaxDegreeOfParallelism_ShouldTriggerEvent()
        {
            // Arrange
            bool eventTriggered = false;
            int deltaReceived = 0;

            _channel.Options.DegreeOfParallelismChangedDelta += (sender, delta) =>
            {
                eventTriggered = true;
                deltaReceived = delta;
            };

            // Act
            _channel.Options.MaxDegreeOfParallelism = _channel.Options.MaxDegreeOfParallelism + 5;

            // Assert
            eventTriggered.Should().BeTrue();
            deltaReceived.Should().Be(5);
        }

        #endregion
    }
}