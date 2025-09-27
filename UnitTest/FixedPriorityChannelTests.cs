using Requests.Channel;

namespace UnitTest
{
    /// <summary>
    /// Comprehensive test suite for the FixedPriorityChannel implementation.
    /// Tests fixed priority levels, channel behavior, and performance characteristics.
    /// </summary>
    [TestFixture]
    public class FixedPriorityChannelTests
    {
        private FixedPriorityChannel<string> _channel = null!;
        private const int DefaultPriorityLevels = 5;

        [SetUp]
        public void SetUp()
        {
            _channel = new FixedPriorityChannel<string>(DefaultPriorityLevels);
        }

        [TearDown]
        public void TearDown()
        {
            _channel?.Writer?.TryComplete();
        }

        #region Constructor and Basic Properties Tests

        [Test]
        public void Constructor_WithValidPriorityCount_ShouldInitializeCorrectly()
        {
            // Arrange & Act
            FixedPriorityChannel<string> channel = new(10);

            // Assert
            channel.Should().NotBeNull();
            channel.Reader.Should().NotBeNull();
            channel.Writer.Should().NotBeNull();
            channel.Options.Should().NotBeNull();
            channel.Count.Should().Be(0);
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
        public void Options_DefaultValues_ShouldBeCorrect()
        {
            // Act & Assert
            _channel.Options.Should().NotBeNull();
            _channel.Options.MaxDegreeOfParallelism.Should().Be(Environment.ProcessorCount);
            _channel.Options.EasyEndToken.Should().NotBeNull();
        }

        #endregion

        #region Write Operations Tests

        [Test]
        public async Task Writer_TryWrite_ValidPriority_ShouldAddItemToChannel()
        {
            // Arrange
            PriorityItem<string> item = new(2.0f, "test");

            // Act
            bool result = _channel.Writer.TryWrite(item);

            // Assert
            result.Should().BeTrue();
            _channel.Count.Should().Be(1);

            PriorityItem<string> readResult = await _channel.Reader.ReadAsync();
            readResult.Should().Be(item);
        }

        [Test]
        public async Task Writer_WriteAsync_ValidPriority_ShouldAddItemToChannel()
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
        public async Task Writer_TryWrite_MultiplePriorities_ShouldDistributeCorrectly()
        {
            // Arrange
            PriorityItem<string>[] items = new[]
            {
                new PriorityItem<string>(0.0f, "priority0"),
                new PriorityItem<string>(2.0f, "priority2"),
                new PriorityItem<string>(4.0f, "priority4"),
                new PriorityItem<string>(1.0f, "priority1"),
                new PriorityItem<string>(3.0f, "priority3")
            };

            // Act
            foreach (PriorityItem<string>? item in items)
            {
                _channel.Writer.TryWrite(item).Should().BeTrue();
            }

            // Assert
            _channel.Count.Should().Be(5);

            // Read items - should come out in priority order (0, 1, 2, 3, 4)
            List<string> results = new();
            while (_channel.Reader.TryRead(out PriorityItem<string>? item))
            {
                results.Add(item.Item);
            }

            results.Should().Equal("priority0", "priority1", "priority2", "priority3", "priority4");
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

        #endregion

        #region Read Operations Tests

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
            PriorityItem<string>[] items = new[]
            {
                new PriorityItem<string>(3.0f, "low"),
                new PriorityItem<string>(0.0f, "high"),
                new PriorityItem<string>(1.0f, "medium")
            };

            foreach (PriorityItem<string>? item in items)
                await _channel.Writer.WriteAsync(item);

            // Act
            PriorityItem<string> first = await _channel.Reader.ReadAsync();
            PriorityItem<string> second = await _channel.Reader.ReadAsync();
            PriorityItem<string> third = await _channel.Reader.ReadAsync();

            // Assert
            first.Item.Should().Be("high");   // Priority 0
            second.Item.Should().Be("medium"); // Priority 1
            third.Item.Should().Be("low");     // Priority 3
        }

        [Test]
        public async Task Reader_ReadAsync_SamePriority_ShouldMaintainFIFOOrder()
        {
            // Arrange
            PriorityItem<string>[] items = new[]
            {
                new PriorityItem<string>(2.0f, "first"),
                new PriorityItem<string>(2.0f, "second"),
                new PriorityItem<string>(2.0f, "third")
            };

            foreach (PriorityItem<string>? item in items)
                await _channel.Writer.WriteAsync(item);

            // Act
            List<string> results = new();
            while (_channel.Reader.TryRead(out PriorityItem<string>? item))
                results.Add(item.Item);

            // Assert
            results.Should().Equal("first", "second", "third");
        }

        [Test]
        public async Task Reader_WaitToReadAsync_EmptyChannel_ShouldWaitForData()
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

        #region Priority System Tests

        [Test]
        public async Task PrioritySystem_FixedLevels_ShouldRespectPriorityBoundaries()
        {
            // Arrange - Test with explicit priority levels
            FixedPriorityChannel<string> channel = new(3); // 3 priority levels: 0, 1, 2

            PriorityItem<string>[] items = new[]
            {
                new PriorityItem<string>(2.0f, "low"),     // Highest priority index
                new PriorityItem<string>(0.0f, "high"),   // Lowest priority index (highest priority)
                new PriorityItem<string>(1.0f, "medium")  // Middle priority index
            };

            // Act
            foreach (PriorityItem<string>? item in items)
                await channel.Writer.WriteAsync(item);

            List<string> results = new();
            while (channel.Reader.TryRead(out PriorityItem<string>? item))
                results.Add(item.Item);

            // Assert
            results.Should().Equal("high", "medium", "low");
            channel.Writer.TryComplete();
        }

        [Test]
        public async Task PrioritySystem_IntegerPriorities_ShouldWorkCorrectly()
        {
            // Arrange
            FixedPriorityChannel<int> intChannel = new(DefaultPriorityLevels);
            PriorityItem<int>[] items = new[]
            {
                new PriorityItem<int>(4, 400),
                new PriorityItem<int>(0, 100),  // Highest priority
                new PriorityItem<int>(2, 200),
                new PriorityItem<int>(1, 150),
                new PriorityItem<int>(3, 300)
            };

            foreach (PriorityItem<int>? item in items)
                await intChannel.Writer.WriteAsync(item);

            // Act
            List<int> results = new();
            while (intChannel.Reader.TryRead(out PriorityItem<int>? item))
                results.Add(item.Item);

            // Assert
            results.Should().Equal(100, 150, 200, 300, 400);

            // Clean up
            intChannel.Writer.TryComplete();
        }

        [Test]
        public async Task PrioritySystem_MixedWithSamePriority_ShouldMaintainOrderWithinPriority()
        {
            // Arrange
            PriorityItem<string>[] items = new[]
            {
                new PriorityItem<string>(1.0f, "medium1"),
                new PriorityItem<string>(0.0f, "high1"),
                new PriorityItem<string>(1.0f, "medium2"),
                new PriorityItem<string>(0.0f, "high2"),
                new PriorityItem<string>(1.0f, "medium3")
            };

            foreach (PriorityItem<string>? item in items)
                await _channel.Writer.WriteAsync(item);

            // Act
            List<string> results = new();
            while (_channel.Reader.TryRead(out PriorityItem<string>? item))
                results.Add(item.Item);

            // Assert
            results.Should().Equal("high1", "high2", "medium1", "medium2", "medium3");
        }

        #endregion

        #region TryRemove Tests

        [Test]
        public async Task TryRemove_ExistingItem_ShouldRemoveAndReturnTrue()
        {
            // Arrange
            PriorityItem<string> item1 = new(0.0f, "item1");
            PriorityItem<string> item2 = new(1.0f, "item2");
            PriorityItem<string> item3 = new(2.0f, "item3");

            await _channel.Writer.WriteAsync(item1);
            await _channel.Writer.WriteAsync(item2);
            await _channel.Writer.WriteAsync(item3);

            int initialCount = _channel.Count;
            PriorityItem<string>[] initialItems = _channel.ToArray();

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
            Task<bool> completionTask = Task.Run(() => _channel.Writer.TryComplete());
            bool completed = await completionTask.WaitAsync(TimeSpan.FromSeconds(2));

            // Assert
            completed.Should().BeTrue("TryComplete should succeed and not deadlock");
            _channel.Reader.Completion.IsCompleted.Should().BeTrue();
        }

        [Test]
        public async Task TryRemove_AllItems_ThenTryComplete_ShouldNotDeadlock()
        {
            // Arrange
            PriorityItem<string>[] items = new[]
            {
                new PriorityItem<string>(0.0f, "item1"),
                new PriorityItem<string>(1.0f, "item2"),
                new PriorityItem<string>(2.0f, "item3")
            };

            foreach (PriorityItem<string>? item in items)
                await _channel.Writer.WriteAsync(item);

            // Act - Remove all items
            foreach (PriorityItem<string>? item in items)
            {
                bool removed = _channel.TryRemove(item);
                removed.Should().BeTrue();
            }
            _channel.Count.Should().Be(0);

            // Act - Try to complete (this should not deadlock)
            Task<bool> completionTask = Task.Run(() => _channel.Writer.TryComplete());
            bool completed = await completionTask.WaitAsync(TimeSpan.FromSeconds(2));

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
        public async Task ToArray_WithItems_ShouldReturnAllItems()
        {
            // Arrange
            PriorityItem<string>[] items = new[]
            {
                new PriorityItem<string>(2.0f, "item2"),
                new PriorityItem<string>(0.0f, "item0"),
                new PriorityItem<string>(1.0f, "item1")
            };

            foreach (PriorityItem<string>? item in items)
                await _channel.Writer.WriteAsync(item);

            // Act
            PriorityItem<string>[] array = _channel.ToArray();

            // Assert
            array.Should().HaveCount(3);
            array.Should().Contain(items);
        }

        #endregion

        #region Parallel Operations Tests

        [Test]
        public async Task RunParallelReader_ShouldProcessAllItems()
        {
            // Arrange
            const int itemCount = 50;
            List<string> processedItems = new();
            object lockObject = new();

            // Add items to channel
            for (int i = 0; i < itemCount; i++)
            {
                await _channel.Writer.WriteAsync(new PriorityItem<string>(i % DefaultPriorityLevels, $"item{i}"));
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
        public async Task RunParallelReader_WithException_ShouldHandleGracefully()
        {
            // Arrange
            const int itemCount = 10;
            int processedCount = 0;

            for (int i = 0; i < itemCount; i++)
            {
                await _channel.Writer.WriteAsync(new PriorityItem<string>(i % DefaultPriorityLevels, $"item{i}"));
            }
            _channel.Writer.TryComplete();

            // Act & Assert
            Func<Task> act = () => _channel.RunParallelReader((item, ct) =>
            {
                Interlocked.Increment(ref processedCount);
                if (item.Item == "item5")
                {
                    throw new InvalidOperationException("Test exception");
                }
                return ValueTask.CompletedTask;
            });

            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("Test exception");

            processedCount.Should().BeGreaterThan(0);
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
        public async Task ConcurrentWriteRead_MultipleThreads_ShouldMaintainPriorityOrder()
        {
            //Test missing
        }

        [Test]
        public async Task ConcurrentTryRemove_MultipleThreads_ShouldMaintainConsistency()
        {
            // Test Missing
        }

        #endregion

        #region Error Handling Tests


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

        #region Performance Comparison Tests

        [Test]
        [Category("Performance")]
        public async Task Performance_FixedVsDynamic_ShouldShowExpectedCharacteristics()
        {
            // Arrange
            const int itemCount = 10000;
            FixedPriorityChannel<int> fixedChannel = new(DefaultPriorityLevels);
            DynamicPriorityChannel<int> dynamicChannel = new();

            Random random = new(42);
            PriorityItem<int>[] items = Enumerable.Range(0, itemCount)
                .Select(i => new PriorityItem<int>(random.Next(0, DefaultPriorityLevels), i))
                .ToArray();

            // Test Fixed Channel
            Stopwatch fixedStopwatch = Stopwatch.StartNew();
            foreach (PriorityItem<int>? item in items)
                await fixedChannel.Writer.WriteAsync(item);

            while (fixedChannel.Reader.TryRead(out _)) { }
            fixedStopwatch.Stop();

            // Test Dynamic Channel
            Stopwatch dynamicStopwatch = Stopwatch.StartNew();
            foreach (PriorityItem<int>? item in items)
                await dynamicChannel.Writer.WriteAsync(item);

            while (dynamicChannel.Reader.TryRead(out _)) { }
            dynamicStopwatch.Stop();

            // Assert
            fixedStopwatch.ElapsedMilliseconds.Should().BeLessThan(5000, "Fixed channel should be reasonably fast");
            dynamicStopwatch.ElapsedMilliseconds.Should().BeLessThan(5000, "Dynamic channel should be reasonably fast");

            // Clean up
            fixedChannel.Writer.TryComplete();
            dynamicChannel.Writer.TryComplete();
        }

        #endregion

        #region Options Modification Tests

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

            int originalValue = _channel.Options.MaxDegreeOfParallelism;

            // Act
            _channel.Options.MaxDegreeOfParallelism = originalValue + 3;

            // Assert
            eventTriggered.Should().BeTrue();
            deltaReceived.Should().Be(3);
        }

        [Test]
        public void Options_SetSameValue_ShouldNotTriggerEvent()
        {
            // Arrange
            bool eventTriggered = false;

            _channel.Options.DegreeOfParallelismChangedDelta += (sender, delta) =>
            {
                eventTriggered = true;
            };

            int originalValue = _channel.Options.MaxDegreeOfParallelism;

            // Act
            _channel.Options.MaxDegreeOfParallelism = originalValue;

            // Assert
            eventTriggered.Should().BeFalse();
        }

        #endregion
    }
}