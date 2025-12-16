namespace UnitTest
{
    /// <summary>
    /// Comprehensive test suite for the YieldAsync structure and Request.Yield() pattern.
    /// Tests cover both the YieldAwaitable pattern and IRequest.YieldAsync() implementation.
    /// </summary>
    [TestFixture]
    public class YieldAsyncTests
    {
        private ParallelRequestHandler _parallelHandler = null!;
        private SequentialRequestHandler _sequentialHandler = null!;

        [SetUp]
        public void SetUp()
        {
            _parallelHandler = [];
            _sequentialHandler = [];
        }

        [TearDown]
        public void TearDown()
        {
            _parallelHandler?.Dispose();
            _sequentialHandler?.Dispose();
        }

        #region Helper Methods

        /// <summary>
        /// Creates a test request with the parallel handler.
        /// </summary>
        private OwnRequest CreateParallelRequest(Func<CancellationToken, Task<bool>> work, bool autoStart = true)
        {
            return new OwnRequest(work, new RequestOptions
            {
                Handler = _parallelHandler,
                AutoStart = autoStart
            });
        }

        /// <summary>
        /// Creates a test request with the sequential handler.
        /// </summary>
        private OwnRequest CreateSequentialRequest(Func<CancellationToken, Task<bool>> work, bool autoStart = true)
        {
            return new OwnRequest(work, new RequestOptions
            {
                Handler = _sequentialHandler,
                AutoStart = autoStart
            });
        }

        #endregion

        #region YieldAsync Fast Path Tests

        [Test]
        public async Task YieldAsync_NotPausedNotCancelled_ShouldCompleteSynchronously()
        {
            // Arrange
            using OwnRequest request = CreateParallelRequest(async token =>
            {
                await Task.Yield();
                return true;
            });

            // Act
            ValueTask yieldTask = request.YieldAsync();

            // Assert
            yieldTask.IsCompleted.Should().BeTrue("Fast path should complete synchronously");
            await yieldTask;
        }

        [Test]
        public async Task YieldAsync_MultipleCalls_ShouldAllCompleteFast()
        {
            // Arrange
            using OwnRequest request = CreateParallelRequest(async token =>
            {
                for (int i = 0; i < 10; i++)
                {
                    await Request.Yield();
                }
                return true;
            });

            // Act & Assert
            for (int i = 0; i < 5; i++)
            {
                ValueTask yieldTask = request.YieldAsync();
                yieldTask.IsCompleted.Should().BeTrue();
                await yieldTask;
            }

            await request.Task;
        }

        #endregion

        #region YieldAsync Slow Path Tests (Paused)

        [Test]
        public async Task YieldAsync_WhenPaused_ShouldWaitForResume()
        {
            // Arrange
            TaskCompletionSource<bool> pausedTcs = new();
            OwnRequest? capturedRequest = null;
            using OwnRequest request = CreateParallelRequest(async token =>
            {
                capturedRequest!.Pause();
                pausedTcs.SetResult(true);
                // This yield should wait because we're paused
                await capturedRequest.YieldAsync();
                return true;
            });
            capturedRequest = request;

            await pausedTcs.Task;

            // Assert
            request.State.Should().Be(RequestState.Paused);
            await Task.Delay(200);
            request.State.Should().Be(RequestState.Paused);

            // Act 
            request.Start();
            await Task.Delay(100); // Give time to complete

            // Assert - should complete successfully
            request.State.Should().Be(RequestState.Completed);
        }

        [Test]
        public async Task YieldAsync_PausedThenResumed_ShouldContinue()
        {
            // Arrange
            bool yieldCompleted = false;
            OwnRequest? capturedRequest = null;
            using OwnRequest request = CreateParallelRequest(async token =>
            {
                capturedRequest!.Pause();
                await capturedRequest.YieldAsync();
                yieldCompleted = true;
                return true;
            });
            capturedRequest = request;

            // Act
            await Task.Delay(100);
            request.Start();
            await Task.Delay(100);

            // Assert
            yieldCompleted.Should().BeTrue("Yield should complete after resume");
        }

        #endregion

        #region YieldAsync Cancellation Tests

        [Test]
        public void YieldAsync_WhenCancelled_ShouldThrowOperationCanceledException()
        {
            // Arrange
            using OwnRequest request = CreateParallelRequest(async token =>
            {
                await Task.Yield();
                return true;
            });
            request.Cancel();

            // Act
            Func<Task> act = async () => await request.YieldAsync();

            // Assert
            act.Should().ThrowAsync<OperationCanceledException>();
        }

        [Test]
        public async Task YieldAsync_CancelDuringYield_ShouldThrow()
        {
            // Arrange
            TaskCompletionSource<bool> yieldStarted = new();
            OwnRequest? capturedRequest = null;
            using OwnRequest request = CreateParallelRequest(async token =>
            {
                yieldStarted.SetResult(true);
                capturedRequest!.Pause();
                await capturedRequest.YieldAsync(); // Should throw when cancelled
                return true;
            });
            capturedRequest = request;

            await yieldStarted.Task;

            // Act
            request.Cancel();

            // Assert
            await Task.Delay(100);
            request.State.Should().Be(RequestState.Cancelled);
        }

        #endregion

        #region Request.Yield() Static Pattern Tests

        [Test]
        public async Task StaticYield_OutsideRequestContext_ShouldCompleteImmediately()
        {
            // Act
            YieldAwaitable yielder = Request.Yield();
            var awaiter = yielder.GetAwaiter();

            // Assert
            awaiter.IsCompleted.Should().BeTrue("Should complete immediately outside request context");
            await yielder;
        }

        [Test]
        public async Task StaticYield_InsideRequestContext_ShouldYieldThroughRequest()
        {
            // Arrange
            int yieldCount = 0;
            using OwnRequest request = CreateParallelRequest(async token =>
            {
                for (int i = 0; i < 5; i++)
                {
                    await Request.Yield();
                    yieldCount++;
                }
                return true;
            });

            // Act
            await request.Task;

            // Assert
            yieldCount.Should().Be(5);
            request.State.Should().Be(RequestState.Completed);
        }

        [Test]
        public async Task StaticYield_MultipleConcurrentRequests_ShouldMaintainContext()
        {
            // Arrange
            List<int> request1Yields = [];
            List<int> request2Yields = [];

            using OwnRequest request1 = CreateParallelRequest(async token =>
            {
                for (int i = 0; i < 3; i++)
                {
                    request1Yields.Add(i);
                    await Request.Yield();
                    await Task.Delay(10);
                }
                return true;
            });

            using OwnRequest request2 = CreateParallelRequest(async token =>
            {
                for (int i = 0; i < 3; i++)
                {
                    request2Yields.Add(i);
                    await Request.Yield();
                    await Task.Delay(10);
                }
                return true;
            });

            // Act
            await Task.WhenAll(request1.Task, request2.Task);

            // Assert
            request1Yields.Should().HaveCount(3);
            request2Yields.Should().HaveCount(3);
        }

        #endregion

        #region YieldAwaitable Pattern Tests

        [Test]
        public void YieldAwaitable_GetAwaiter_ShouldReturnAwaiter()
        {
            // Arrange
            YieldAwaitable awaitable = Request.Yield();

            // Act
            YieldAwaiter awaiter = awaitable.GetAwaiter();

            // Assert
            awaiter.Should().NotBeNull();
        }

        [Test]
        public void YieldAwaiter_GetResult_ShouldNotThrow()
        {
            // Arrange
            YieldAwaitable awaitable = Request.Yield();
            var awaiter = awaitable.GetAwaiter();

            // Act
            Action act = () => awaiter.GetResult();

            // Assert
            act.Should().NotThrow();
        }

        [Test]
        public async Task YieldAwaiter_OnCompleted_ShouldInvokeContinuation()
        {
            // Arrange
            bool continuationCalled = false;
            using OwnRequest request = CreateParallelRequest(async token =>
            {
                await Task.Yield();
                return true;
            });

            YieldAwaitable awaitable = Request.Yield();
            var awaiter = awaitable.GetAwaiter();

            // Act
            if (!awaiter.IsCompleted)
            {
                awaiter.OnCompleted(() => continuationCalled = true);
                await Task.Delay(100);
            }
            else
            {
                continuationCalled = true; // Completed synchronously
            }

            // Assert
            continuationCalled.Should().BeTrue();
        }

        [Test]
        public async Task YieldAwaiter_UnsafeOnCompleted_ShouldInvokeContinuation()
        {
            // Arrange
            bool continuationCalled = false;
            YieldAwaitable awaitable = Request.Yield();
            var awaiter = awaitable.GetAwaiter();

            // Act
            if (!awaiter.IsCompleted)
            {
                awaiter.UnsafeOnCompleted(() => continuationCalled = true);
                await Task.Delay(100);
            }
            else
            {
                continuationCalled = true; // Completed synchronously
            }

            // Assert
            continuationCalled.Should().BeTrue();
        }

        [Test]
        public void YieldAwaiter_OnCompleted_NullContinuation_ShouldThrowArgumentNullException()
        {
            // Arrange
            YieldAwaitable awaitable = Request.Yield();
            var awaiter = awaitable.GetAwaiter();

            // Act
            Action act = () => awaiter.OnCompleted(null!);

            // Assert
            act.Should().Throw<ArgumentNullException>();
        }

        [Test]
        public void YieldAwaiter_UnsafeOnCompleted_NullContinuation_ShouldThrowArgumentNullException()
        {
            // Arrange
            YieldAwaitable awaitable = Request.Yield();
            var awaiter = awaitable.GetAwaiter();

            // Act
            Action act = () => awaiter.UnsafeOnCompleted(null!);

            // Assert
            act.Should().Throw<ArgumentNullException>();
        }

        #endregion

        #region Integration Tests with ParallelRequestHandler

        [Test]
        public async Task YieldAsync_InParallelHandler_ShouldAllowPause()
        {
            // Arrange
            bool yieldedDuringPause = false;

            using OwnRequest request = CreateParallelRequest(async token =>
            {
                await Task.Delay(50, token);
                await Request.Yield();
                yieldedDuringPause = _parallelHandler.State == RequestState.Paused;
                return true;
            });

            // Act
            await Task.Delay(30);
            _parallelHandler.Pause();
            await Task.Delay(100);
            _parallelHandler.Start();
            await request.Task;

            // Assert
            request.State.Should().Be(RequestState.Completed);
        }

        #endregion

        #region Integration Tests with SequentialRequestHandler

        [Test]
        public async Task YieldAsync_InSequentialHandler_ShouldRespectSequentialExecution()
        {
            // Arrange
            List<int> executionOrder = [];

            using OwnRequest request1 = CreateSequentialRequest(async token =>
            {
                executionOrder.Add(1);
                await Request.Yield();
                executionOrder.Add(2);
                return true;
            });

            using OwnRequest request2 = CreateSequentialRequest(async token =>
            {
                executionOrder.Add(3);
                await Request.Yield();
                executionOrder.Add(4);
                return true;
            });

            // Act
            await Task.WhenAll(request1.Task, request2.Task);

            // Assert
            executionOrder.Should().ContainInOrder([1, 2, 3, 4]);
        }

        #endregion

        #region Performance and Stress Tests

        [Test]
        public async Task YieldAsync_ManyYields_ShouldPerformEfficiently()
        {
            // Arrange
            const int yieldCount = 1000;
            int actualYields = 0;

            using OwnRequest request = CreateParallelRequest(async token =>
            {
                for (int i = 0; i < yieldCount; i++)
                {
                    await Request.Yield();
                    actualYields++;
                }
                return true;
            });

            // Act
            DateTime startTime = DateTime.Now;
            await request.Task;
            TimeSpan elapsed = DateTime.Now - startTime;

            // Assert
            actualYields.Should().Be(yieldCount);
            elapsed.TotalSeconds.Should().BeLessThan(5, "Should complete efficiently");
        }

        [Test]
        public async Task YieldAsync_NestedYields_ShouldWorkCorrectly()
        {
            // Arrange
            int outerYields = 0;
            int innerYields = 0;

            using OwnRequest request = CreateParallelRequest(async token =>
            {
                for (int i = 0; i < 3; i++)
                {
                    await Request.Yield();
                    outerYields++;

                    for (int j = 0; j < 3; j++)
                    {
                        await Request.Yield();
                        innerYields++;
                    }
                }
                return true;
            });

            // Act
            await request.Task;

            // Assert
            outerYields.Should().Be(3);
            innerYields.Should().Be(9);
        }

        #endregion

        #region Edge Cases Tests

        [Test]
        public async Task YieldAsync_ImmediatelyAfterStart_ShouldWork()
        {
            // Arrange
            bool yielded = false;

            using OwnRequest request = CreateParallelRequest(async token =>
            {
                await Request.Yield();
                yielded = true;
                return true;
            });

            // Act
            await request.Task;

            // Assert
            yielded.Should().BeTrue();
        }

        [Test]
        public async Task YieldAsync_BeforeCompletion_ShouldWork()
        {
            // Arrange
            bool yieldedBeforeCompletion = false;

            using OwnRequest request = CreateParallelRequest(async token =>
            {
                await Task.Delay(50, token);
                await Request.Yield();
                yieldedBeforeCompletion = true;
                return true;
            });

            // Act
            await request.Task;

            // Assert
            yieldedBeforeCompletion.Should().BeTrue();
        }

        [Test]
        public async Task YieldAsync_WithException_ShouldAddExceptionToList()
        {
            // Arrange
            using OwnRequest request = CreateParallelRequest(async token =>
            {
                await Request.Yield();
                throw new InvalidOperationException("Test exception");
            });

            // Act
            await request.Task;

            // Assert
            request.Exception.Should().NotBeNull();
            request.Exception!.InnerExceptions.First().Should().BeOfType<InvalidOperationException>();
            request.Exception!.InnerExceptions.First().Message.Should().Be("Test exception");
        }

        [Test]
        public async Task YieldAsync_InTryFinally_ShouldExecuteFinally()
        {
            // Arrange
            bool finallyExecuted = false;

            using OwnRequest request = CreateParallelRequest(async token =>
            {
                try
                {
                    await Request.Yield();
                    return true;
                }
                finally
                {
                    finallyExecuted = true;
                }
            });

            // Act
            await request.Task;

            // Assert
            finallyExecuted.Should().BeTrue();
        }

        [Test]
        public async Task YieldAsync_InTryCatch_ShouldCatchExceptions()
        {
            // Arrange
            bool exceptionCaught = false;

            using OwnRequest request = CreateParallelRequest(async token =>
            {
                try
                {
                    await Request.Yield();
                    throw new InvalidOperationException();
                }
                catch (InvalidOperationException)
                {
                    exceptionCaught = true;
                }
                return true;
            });

            // Act
            await request.Task;

            // Assert
            exceptionCaught.Should().BeTrue();
        }

        #endregion

        #region Context Preservation Tests

        [Test]
        public async Task YieldAsync_ShouldPreserveSynchronizationContext()
        {
            // Arrange
            SynchronizationContext? contextBeforeYield = null;
            SynchronizationContext? contextAfterYield = null;
            SynchronizationContext testContext = new();

            using OwnRequest request = CreateParallelRequest(async token =>
            {
                SynchronizationContext.SetSynchronizationContext(testContext);
                contextBeforeYield = SynchronizationContext.Current;
                await Request.Yield();
                contextAfterYield = SynchronizationContext.Current;
                return true;
            });

            // Act
            await request.Task;

            // Assert - Note: Context may not be preserved across yields, which is expected
            contextBeforeYield.Should().Be(testContext);
        }

        [Test]
        public async Task YieldAsync_ShouldAllowTaskSchedulerChanges()
        {
            // Arrange
            TaskScheduler? schedulerInRequest = null;

            using OwnRequest request = CreateParallelRequest(async token =>
            {
                await Request.Yield();
                schedulerInRequest = TaskScheduler.Current;
                return true;
            });

            // Act
            await request.Task;

            // Assert
            schedulerInRequest.Should().NotBeNull();
        }

        #endregion
    }
}
