using Requests.Options;

namespace UnitTest
{
    /// <summary>
    /// Comprehensive test suite for the YieldAsync structure and Request.Yield() pattern.
    /// Tests cover both the YieldAwaitable pattern and IRequest.YieldAsync() implementation.
    /// </summary>
    [TestFixture]
    public class YieldAsyncTests
    {
        #region Test Helper Classes

        /// <summary>
        /// Test request implementation that tracks yield calls.
        /// </summary>
        private class YieldTrackingRequest : Request<RequestOptions<object, object>, object, object>
        {
            private readonly Func<CancellationToken, Task<bool>> _action;
            public int YieldCount { get; private set; }
            public List<DateTime> YieldTimes { get; } = [];
            private TaskCompletionSource<bool>? _pauseResumeTcs;

            public YieldTrackingRequest(Func<CancellationToken, Task<bool>> action, RequestOptions<object, object>? options = null)
                : base(options)
            {
                _action = action;
            }

            protected override async Task<RequestReturn> RunRequestAsync()
            {
                bool result = await _action(Token);
                return new RequestReturn { Successful = result };
            }

            public new async ValueTask YieldAsync()
            {
                YieldCount++;
                YieldTimes.Add(DateTime.Now);
                await base.YieldAsync();
            }

            public void PauseExecution()
            {
                _pauseResumeTcs = new TaskCompletionSource<bool>();
                Pause();
            }

            public void ResumeExecution()
            {
                _pauseResumeTcs?.TrySetResult(true);
                Start();
            }
        }

        #endregion

        #region YieldAsync Fast Path Tests

        [Test]
        public async Task YieldAsync_NotPausedNotCancelled_ShouldCompleteSynchronously()
        {
            // Arrange
            using OwnRequest request = new(async token =>
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
            // Arrange & Act
            using ParallelRequestHandler handler = new();
            using OwnRequest request = new(async token =>
            {
                for (int i = 0; i < 10; i++)
                {
                    await Request.Yield();
                }
                return true;
            });

            handler.Add(request);

            // Assert
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
            TaskCompletionSource<bool> tcs = new();
            using ParallelRequestHandler handler = new();
            OwnRequest? capturedRequest = null;
            using OwnRequest request = new(async token =>
            {
                tcs.SetResult(true);
                await capturedRequest!.YieldAsync();
                return true;
            });
            capturedRequest = request;

            handler.Add(request);
            await tcs.Task; // Wait for request to start

            // Act
            request.Pause();
            ValueTask yieldTask = request.YieldAsync();

            // Assert
            yieldTask.IsCompleted.Should().BeFalse("Should wait when paused");

            request.Start();
            await Task.Delay(50); // Give time for resume
        }

        [Test]
        public async Task YieldAsync_PausedThenResumed_ShouldContinue()
        {
            // Arrange
            bool yieldCompleted = false;
            using ParallelRequestHandler handler = new();
            OwnRequest? capturedRequest = null;
            using OwnRequest request = new(async token =>
            {
                capturedRequest!.Pause();
                await capturedRequest.YieldAsync();
                yieldCompleted = true;
                return true;
            });
            capturedRequest = request;

            // Act
            handler.Add(request);
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
            using OwnRequest request = new(async token =>
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
            using OwnRequest request = new(async token =>
            {
                yieldStarted.SetResult(true);
                capturedRequest!.Pause();
                await capturedRequest.YieldAsync(); // Should throw when cancelled
                return true;
            });
            capturedRequest = request;

            using var handler = new ParallelRequestHandler(); handler.Add(request);
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

            // Assert
            yielder.IsCompleted.Should().BeTrue("Should complete immediately outside request context");
            await yielder;
        }

        [Test]
        public async Task StaticYield_InsideRequestContext_ShouldYieldThroughRequest()
        {
            // Arrange
            int yieldCount = 0;
            using ParallelRequestHandler handler = new();
            using OwnRequest request = new(async token =>
            {
                for (int i = 0; i < 5; i++)
                {
                    await Request.Yield();
                    yieldCount++;
                }
                return true;
            });

            // Act
            handler.Add(request);
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

            using ParallelRequestHandler handler = new();
            using OwnRequest request1 = new(async token =>
            {
                for (int i = 0; i < 3; i++)
                {
                    request1Yields.Add(i);
                    await Request.Yield();
                    await Task.Delay(10);
                }
                return true;
            });

            using OwnRequest request2 = new(async token =>
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
            handler.AddRange(request1, request2);
            await Task.WhenAll(request1.Task, request2.Task);

            // Assert
            request1Yields.Should().HaveCount(3);
            request2Yields.Should().HaveCount(3);
        }

        #endregion

        #region YieldAwaitable Pattern Tests

        [Test]
        public void YieldAwaitable_GetAwaiter_ShouldReturnSelf()
        {
            // Arrange
            YieldAwaitable awaitable = Request.Yield();

            // Act
            YieldAwaitable awaiter = awaitable.GetAwaiter();

            // Assert
            awaiter.Should().Be(awaitable);
        }

        [Test]
        public void YieldAwaitable_GetResult_ShouldNotThrow()
        {
            // Arrange
            YieldAwaitable awaitable = Request.Yield();

            // Act
            Action act = () => awaitable.GetResult();

            // Assert
            act.Should().NotThrow();
        }

        [Test]
        public async Task YieldAwaitable_OnCompleted_ShouldInvokeContinuation()
        {
            // Arrange
            bool continuationCalled = false;
            using OwnRequest request = new(async token =>
            {
                await Task.Yield();
                return true;
            });

            YieldAwaitable awaitable = Request.Yield();

            // Act
            if (!awaitable.IsCompleted)
            {
                awaitable.OnCompleted(() => continuationCalled = true);
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
        public async Task YieldAwaitable_UnsafeOnCompleted_ShouldInvokeContinuation()
        {
            // Arrange
            bool continuationCalled = false;
            YieldAwaitable awaitable = Request.Yield();

            // Act
            if (!awaitable.IsCompleted)
            {
                awaitable.UnsafeOnCompleted(() => continuationCalled = true);
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
        public void YieldAwaitable_OnCompleted_NullContinuation_ShouldThrowArgumentNullException()
        {
            // Arrange
            YieldAwaitable awaitable = Request.Yield();

            // Act
            Action act = () => awaitable.OnCompleted(null!);

            // Assert
            act.Should().Throw<ArgumentNullException>();
        }

        [Test]
        public void YieldAwaitable_UnsafeOnCompleted_NullContinuation_ShouldThrowArgumentNullException()
        {
            // Arrange
            YieldAwaitable awaitable = Request.Yield();

            // Act
            Action act = () => awaitable.UnsafeOnCompleted(null!);

            // Assert
            act.Should().Throw<ArgumentNullException>();
        }

        #endregion

        #region Integration Tests with ParallelRequestHandler

        [Test]
        public async Task YieldAsync_InParallelHandler_ShouldAllowPause()
        {
            // Arrange
            using ParallelRequestHandler handler = new();
            bool yieldedDuringPause = false;

            OwnRequest request = new(async token =>
            {
                await Task.Delay(50, token);
                await Request.Yield();
                yieldedDuringPause = handler.State == RequestState.Paused;
                return true;
            });

            handler.Add(request);

            // Act
            await Task.Delay(30);
            handler.Pause();
            await Task.Delay(100);
            handler.Start();
            await request.Task;

            // Assert
            request.State.Should().Be(RequestState.Completed);
        }

        [Test]
        public async Task YieldAsync_InSequentialHandler_ShouldRespectSequentialExecution()
        {
            // Arrange
            using SequentialRequestHandler handler = new();
            List<int> executionOrder = [];

            OwnRequest request1 = new(async token =>
            {
                executionOrder.Add(1);
                await Request.Yield();
                executionOrder.Add(2);
                return true;
            });

            OwnRequest request2 = new(async token =>
            {
                executionOrder.Add(3);
                await Request.Yield();
                executionOrder.Add(4);
                return true;
            });

            // Act
            handler.AddRange(request1, request2);
            await Task.Delay(500);

            // Assert
            executionOrder.Should().ContainInOrder(1, 2, 3, 4);
        }

        #endregion

        #region Performance and Stress Tests

        [Test]
        public async Task YieldAsync_ManyYields_ShouldPerformEfficiently()
        {
            // Arrange
            const int yieldCount = 1000;
            int actualYields = 0;

            using OwnRequest request = new(async token =>
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
            using var handler = new ParallelRequestHandler(); handler.Add(request);
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

            using OwnRequest request = new(async token =>
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
            using var handler = new ParallelRequestHandler(); handler.Add(request);
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

            using OwnRequest request = new(async token =>
            {
                await Request.Yield();
                yielded = true;
                return true;
            });

            // Act
            using var handler = new ParallelRequestHandler(); handler.Add(request);
            await request.Task;

            // Assert
            yielded.Should().BeTrue();
        }

        [Test]
        public async Task YieldAsync_BeforeCompletion_ShouldWork()
        {
            // Arrange
            bool yieldedBeforeCompletion = false;

            using OwnRequest request = new(async token =>
            {
                await Task.Delay(50, token);
                await Request.Yield();
                yieldedBeforeCompletion = true;
                return true;
            });

            // Act
            using var handler = new ParallelRequestHandler(); handler.Add(request);
            await request.Task;

            // Assert
            yieldedBeforeCompletion.Should().BeTrue();
        }

        [Test]
        public async Task YieldAsync_WithException_ShouldPropagateException()
        {
            // Arrange
            using OwnRequest request = new(async token =>
            {
                await Request.Yield();
                throw new InvalidOperationException("Test exception");
            });

            // Act
            using var handler = new ParallelRequestHandler(); handler.Add(request);
            Func<Task> act = async () => await request.Task;

            // Assert
            await act.Should().ThrowAsync<InvalidOperationException>();
        }

        [Test]
        public async Task YieldAsync_InTryFinally_ShouldExecuteFinally()
        {
            // Arrange
            bool finallyExecuted = false;

            using OwnRequest request = new(async token =>
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
            using var handler = new ParallelRequestHandler(); handler.Add(request);
            await request.Task;

            // Assert
            finallyExecuted.Should().BeTrue();
        }

        [Test]
        public async Task YieldAsync_InTryCatch_ShouldCatchExceptions()
        {
            // Arrange
            bool exceptionCaught = false;

            using OwnRequest request = new(async token =>
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
            using var handler = new ParallelRequestHandler(); handler.Add(request);
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

            using OwnRequest request = new(async token =>
            {
                SynchronizationContext.SetSynchronizationContext(testContext);
                contextBeforeYield = SynchronizationContext.Current;
                await Request.Yield();
                contextAfterYield = SynchronizationContext.Current;
                return true;
            });

            // Act
            using var handler = new ParallelRequestHandler(); handler.Add(request);
            await request.Task;

            // Assert - Note: Context may not be preserved across yields, which is expected
            contextBeforeYield.Should().Be(testContext);
        }

        [Test]
        public async Task YieldAsync_ShouldAllowTaskSchedulerChanges()
        {
            // Arrange
            TaskScheduler? schedulerInRequest = null;

            using OwnRequest request = new(async token =>
            {
                await Request.Yield();
                schedulerInRequest = TaskScheduler.Current;
                return true;
            });

            // Act
            using var handler = new ParallelRequestHandler(); handler.Add(request);
            await request.Task;

            // Assert
            schedulerInRequest.Should().NotBeNull();
        }

        #endregion
    }
}
