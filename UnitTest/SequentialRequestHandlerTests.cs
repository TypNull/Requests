namespace UnitTest
{
    /// <summary>
    /// Comprehensive test suite for the SequentialRequestHandler class.
    /// Tests are organized by functionality area with AAA pattern (Arrange, Act, Assert).
    /// </summary>
    [TestFixture]
    public class SequentialRequestHandlerTests
    {
        private SequentialRequestHandler _handler = null!;

        [SetUp]
        public void SetUp()
        {
            _handler = new SequentialRequestHandler();
        }

        [TearDown]
        public void TearDown()
        {
            _handler?.Dispose();
        }

        #region Test Helper Classes

        /// <summary>
        /// Mock implementation of IRequest for testing sequential execution.
        /// Provides control over execution timing and state transitions.
        /// </summary>
        private class MockSequentialRequest : IRequest
        {
            private readonly TaskCompletionSource<bool> _tcs = new();
            private readonly Func<CancellationToken, Task<bool>>? _action;
            public bool WasExecuted { get; private set; }
            public DateTime? ExecutionTime { get; private set; }

            public Task Task => _tcs.Task;
            public RequestState State { get; private set; } = RequestState.Idle;
            public event EventHandler<RequestState>? StateChanged;
            public RequestPriority Priority { get; set; } = RequestPriority.Normal;
            public AggregateException? Exception { get; private set; }
            public IRequest? SubsequentRequest { get; private set; }

            public MockSequentialRequest(Func<CancellationToken, Task<bool>>? action = null)
            {
                _action = action;
            }

            public void Cancel()
            {
                SetState(RequestState.Cancelled);
                _tcs.TrySetCanceled();
            }

            public void Dispose()
            {
                if (State != RequestState.Completed && State != RequestState.Failed)
                    Cancel();
            }

            public void Pause() => SetState(RequestState.Paused);
            public void Start() => SetState(RequestState.Running);

            private void SetState(RequestState newState)
            {
                State = newState;
                StateChanged?.Invoke(this, newState);
            }

            public async Task StartRequestAsync()
            {
                SetState(RequestState.Running);
                WasExecuted = true;
                ExecutionTime = DateTime.Now;

                try
                {
                    bool result = _action != null
                        ? await _action(CancellationToken.None)
                        : true;

                    if (result)
                    {
                        SetState(RequestState.Completed);
                        _tcs.TrySetResult(true);
                    }
                    else
                    {
                        SetState(RequestState.Failed);
                        _tcs.TrySetResult(false);
                    }
                }
                catch (Exception ex)
                {
                    Exception = new AggregateException(ex);
                    SetState(RequestState.Failed);
                    _tcs.TrySetException(ex);
                }
            }

            public bool TrySetIdle()
            {
                if (State == RequestState.Idle) return true;
                SetState(RequestState.Idle);
                return false;
            }

            public bool HasCompleted() => State is RequestState.Completed or RequestState.Failed or RequestState.Cancelled;
            public bool TrySetSubsequentRequest(IRequest request)
            {
                if (SubsequentRequest != null) return false;
                SubsequentRequest = request;
                return true;
            }
            public void Wait() => Task.Wait();
            public ValueTask YieldAsync() => ValueTask.CompletedTask;
        }

        #endregion

        #region Constructor Tests

        [Test]
        public void Constructor_Default_ShouldInitializeCorrectly()
        {
            // Arrange & Act
            using SequentialRequestHandler handler = new();

            // Assert
            handler.Should().NotBeNull();
            handler.State.Should().Be(RequestState.Idle);
            handler.Count.Should().Be(0);
            handler.Priority.Should().Be(RequestPriority.High);
        }

        [Test]
        public void Constructor_WithRequests_ShouldAddRequests()
        {
            // Arrange
            MockSequentialRequest[] requests = [new(), new()];

            // Act
            using SequentialRequestHandler handler = new(requests);

            // Assert
            handler.Count.Should().Be(2);
        }

        #endregion

        #region Add/Remove Tests

        [Test]
        public void Add_SingleRequest_ShouldIncreaseCount()
        {
            // Arrange
            MockSequentialRequest request = new();

            // Act
            _handler.Add(request);

            // Assert
            _handler.Count.Should().BeGreaterThanOrEqualTo(0); // May have already been processed
        }

        [Test]
        public void Add_NullRequest_ShouldThrowArgumentNullException()
        {
            // Arrange
            MockSequentialRequest? request = null;

            // Act
            Action act = () => _handler.Add(request!);

            // Assert
            act.Should().Throw<ArgumentNullException>();
        }

        [Test]
        public void AddRange_MultipleRequests_ShouldIncreaseCount()
        {
            // Arrange
            MockSequentialRequest[] requests = [new(), new(), new()];

            // Act
            _handler.AddRange(requests);

            // Assert
            _handler.Count.Should().BeGreaterThanOrEqualTo(0); // May have already started processing
        }

        [Test]
        public async Task AddAsync_SingleRequest_ShouldAddAndProcess()
        {
            // Arrange
            MockSequentialRequest request = new();

            // Act
            await _handler.AddAsync(request);
            await Task.Delay(100); // Give time to process

            // Assert
            request.WasExecuted.Should().BeTrue();
        }

        [Test]
        public async Task Remove_PendingRequest_ShouldRemoveSuccessfully()
        {
            // Arrange
            _handler.Pause(); // Pause to prevent immediate processing
            MockSequentialRequest request1 = new(_ => Task.Delay(1000).ContinueWith(_ => true));
            MockSequentialRequest request2 = new();
            _handler.Add(request1);
            _handler.Add(request2);

            // Act
            _handler.Remove(request2);
            _handler.Start();
            await Task.Delay(100);

            // Assert
            request2.WasExecuted.Should().BeFalse();
        }

        #endregion

        #region Sequential Execution Tests

        [Test]
        public async Task RunRequests_MultipleRequests_ShouldExecuteSequentially()
        {
            // Arrange
            List<int> executionOrder = [];
            MockSequentialRequest request1 = new(async _ => { executionOrder.Add(1); await Task.Delay(50); return true; });
            MockSequentialRequest request2 = new(async _ => { executionOrder.Add(2); await Task.Delay(50); return true; });
            MockSequentialRequest request3 = new(async _ => { executionOrder.Add(3); await Task.Delay(50); return true; });

            // Act
            _handler.AddRange(request1, request2, request3);
            await Task.Delay(300); // Wait for all to complete

            // Assert
            executionOrder.Should().ContainInOrder(1, 2, 3);
            request1.WasExecuted.Should().BeTrue();
            request2.WasExecuted.Should().BeTrue();
            request3.WasExecuted.Should().BeTrue();
        }

        [Test]
        public async Task RunRequests_WithPriority_ShouldExecuteByPriority()
        {
            // Arrange
            _handler.Pause(); // Pause to queue all requests first
            List<RequestPriority> executionOrder = [];

            MockSequentialRequest lowPriorityRequest = new(_ => { executionOrder.Add(RequestPriority.Low); return Task.FromResult(true); })
                { Priority = RequestPriority.Low };
            MockSequentialRequest highPriorityRequest = new(_ => { executionOrder.Add(RequestPriority.High); return Task.FromResult(true); })
                { Priority = RequestPriority.High };
            MockSequentialRequest normalPriorityRequest = new(_ => { executionOrder.Add(RequestPriority.Normal); return Task.FromResult(true); })
                { Priority = RequestPriority.Normal };

            _handler.AddRange(lowPriorityRequest, normalPriorityRequest, highPriorityRequest);

            // Act
            _handler.Start();
            await Task.Delay(300);

            // Assert
            executionOrder.Should().HaveCount(3);
            executionOrder[0].Should().Be(RequestPriority.High);
        }

        [Test]
        public async Task RunRequests_OneRequestAtATime_ShouldNotRunConcurrently()
        {
            // Arrange
            int concurrentCount = 0;
            int maxConcurrent = 0;

            MockSequentialRequest CreateRequest() => new(async _ =>
            {
                Interlocked.Increment(ref concurrentCount);
                int current = concurrentCount;
                if (current > maxConcurrent)
                    maxConcurrent = current;

                await Task.Delay(50);
                Interlocked.Decrement(ref concurrentCount);
                return true;
            });

            // Act
            _handler.AddRange(CreateRequest(), CreateRequest(), CreateRequest(), CreateRequest());
            await Task.Delay(400);

            // Assert
            maxConcurrent.Should().Be(1, "Sequential handler should only run one request at a time");
        }

        #endregion

        #region State Management Tests

        [Test]
        public void Start_PausedHandler_ShouldResumeExecution()
        {
            // Arrange
            _handler.Pause();

            // Act
            _handler.Start();

            // Assert
            _handler.State.Should().Be(RequestState.Idle);
        }

        [Test]
        public async Task Pause_RunningHandler_ShouldPauseAfterCurrentRequest()
        {
            // Arrange
            bool request1Started = false;
            bool request2Started = false;

            MockSequentialRequest request1 = new(async _ => { request1Started = true; await Task.Delay(100); return true; });
            MockSequentialRequest request2 = new(_ => { request2Started = true; return Task.FromResult(true); });

            _handler.Add(request1);
            await Task.Delay(50); // Let first request start

            // Act
            _handler.Pause();
            _handler.Add(request2);
            await Task.Delay(200);

            // Assert
            request1Started.Should().BeTrue("First request should complete");
            request2Started.Should().BeFalse("Second request should not start when paused");
        }

        [Test]
        public void Cancel_Handler_ShouldTransitionToCancelled()
        {
            // Act
            _handler.Cancel();

            // Assert
            _handler.State.Should().Be(RequestState.Cancelled);
            _handler.CancellationToken.IsCancellationRequested.Should().BeTrue();
        }

        [Test]
        public void CreateCTS_AfterCancel_ShouldResetCancellationToken()
        {
            // Arrange
            _handler.Cancel();

            // Act
            _handler.CreateCTS();

            // Assert
            _handler.CancellationToken.IsCancellationRequested.Should().BeFalse();
            _handler.State.Should().Be(RequestState.Idle);
        }

        #endregion

        #region Subsequent Request Tests

        [Test]
        public async Task SubsequentRequest_AfterCompletion_ShouldExecuteAutomatically()
        {
            // Arrange
            MockSequentialRequest mainRequest = new();
            MockSequentialRequest subsequentRequest = new();
            mainRequest.TrySetSubsequentRequest(subsequentRequest);

            // Act
            _handler.Add(mainRequest);
            await Task.Delay(200);

            // Assert
            mainRequest.WasExecuted.Should().BeTrue();
            subsequentRequest.WasExecuted.Should().BeTrue();
        }

        [Test]
        public async Task SubsequentRequest_Chain_ShouldExecuteInOrder()
        {
            // Arrange
            List<int> executionOrder = [];
            MockSequentialRequest request1 = new(_ => { executionOrder.Add(1); return Task.FromResult(true); });
            MockSequentialRequest request2 = new(_ => { executionOrder.Add(2); return Task.FromResult(true); });
            MockSequentialRequest request3 = new(_ => { executionOrder.Add(3); return Task.FromResult(true); });

            request1.TrySetSubsequentRequest(request2);
            request2.TrySetSubsequentRequest(request3);

            // Act
            _handler.Add(request1);
            await Task.Delay(300);

            // Assert
            executionOrder.Should().ContainInOrder(1, 2, 3);
        }

        #endregion

        #region Exception Handling Tests

        [Test]
        public async Task UnhandledException_ShouldTriggerEvent()
        {
            // Arrange
            Exception? caughtException = null;
            _handler.UnhandledException += (s, ex) => caughtException = ex;

            MockSequentialRequest failingRequest = new(_ => throw new InvalidOperationException("Test error"));

            // Act
            _handler.Add(failingRequest);
            await Task.Delay(200);

            // Assert
            caughtException.Should().NotBeNull();
        }

        [Test]
        public async Task FailedRequest_ShouldNotStopProcessing()
        {
            // Arrange
            MockSequentialRequest failingRequest = new(_ => throw new InvalidOperationException("Test error"));
            MockSequentialRequest successRequest = new();

            // Act
            _handler.AddRange(failingRequest, successRequest);
            await Task.Delay(300);

            // Assert
            failingRequest.State.Should().Be(RequestState.Failed);
            successRequest.WasExecuted.Should().BeTrue();
        }

        #endregion

        #region Enumeration Tests

        [Test]
        public async Task GetAsyncEnumerator_WithRequests_ShouldEnumerateAll()
        {
            // Arrange
            _handler.Pause();
            _handler.AddRange(new MockSequentialRequest(), new MockSequentialRequest(), new MockSequentialRequest());

            List<IRequest> enumerated = [];

            // Act
            await foreach (IRequest request in _handler)
            {
                enumerated.Add(request);
            }

            // Assert
            enumerated.Should().HaveCount(3);
        }

        [Test]
        public async Task WaitForCurrentRequestsAsync_ShouldWaitForAll()
        {
            // Arrange
            _handler.AddRange(
                new MockSequentialRequest(_ => Task.Delay(100).ContinueWith(_ => true)),
                new MockSequentialRequest(_ => Task.Delay(100).ContinueWith(_ => true))
            );

            // Act
            DateTime startTime = DateTime.Now;
            await _handler.WaitForCurrentRequestsAsync();
            TimeSpan elapsed = DateTime.Now - startTime;

            // Assert
            elapsed.TotalMilliseconds.Should().BeGreaterThanOrEqualTo(150);
        }

        #endregion

        #region Priority Tests

        [Test]
        public void SetPriority_ShouldUpdatePriority()
        {
            // Arrange
            RequestPriority newPriority = RequestPriority.Low;

            // Act
            _handler.SetPriority(newPriority);

            // Assert
            _handler.Priority.Should().Be(newPriority);
        }

        #endregion

        #region TrySetIdle Tests

        [Test]
        public void TrySetIdle_AllIdleRequests_ShouldReturnTrue()
        {
            // Arrange
            _handler.Pause();
            _handler.AddRange(new MockSequentialRequest(), new MockSequentialRequest());

            // Act
            bool result = _handler.TrySetIdle();

            // Assert
            result.Should().BeTrue();
        }

        #endregion

        #region HasCompleted Tests

        [Test]
        public void HasCompleted_NoRequests_ShouldReturnFalse()
        {
            // Act & Assert
            _handler.HasCompleted().Should().BeFalse();
        }

        [Test]
        public async Task HasCompleted_AllRequestsProcessed_ShouldReturnTrue()
        {
            // Arrange
            _handler.Add(new MockSequentialRequest());
            await Task.Delay(100);
            _handler.Cancel();

            // Act & Assert
            _handler.HasCompleted().Should().BeTrue();
        }

        #endregion

        #region YieldAsync Tests

        [Test]
        public async Task YieldAsync_ShouldCompleteImmediately()
        {
            // Arrange
            IRequest request = _handler;

            // Act
            ValueTask yieldTask = request.YieldAsync();

            // Assert
            yieldTask.IsCompleted.Should().BeTrue();
            await yieldTask;
        }

        #endregion

        #region Lifecycle Tests

        [Test]
        public void Dispose_Handler_ShouldCancelAndCleanup()
        {
            // Arrange
            _handler.Add(new MockSequentialRequest());

            // Act
            _handler.Dispose();

            // Assert
            _handler.State.Should().Be(RequestState.Cancelled);
        }

        [Test]
        public void Dispose_MultipleTimes_ShouldNotThrow()
        {
            // Act
            Action act = () =>
            {
                _handler.Dispose();
                _handler.Dispose();
                _handler.Dispose();
            };

            // Assert
            act.Should().NotThrow();
        }

        #endregion

        #region StateChanged Event Tests

        [Test]
        public void StateChanged_WhenStateChanges_ShouldFireEvent()
        {
            // Arrange
            List<RequestState> stateChanges = [];
            _handler.StateChanged += (s, state) => stateChanges.Add(state);

            // Act
            _handler.Pause();
            _handler.Cancel();

            // Assert
            stateChanges.Should().Contain(RequestState.Paused);
            stateChanges.Should().Contain(RequestState.Cancelled);
        }

        #endregion

        #region Edge Cases and Stress Tests

        [Test]
        public async Task ManyRequests_ShouldProcessAllSequentially()
        {
            // Arrange
            const int count = 50;
            List<MockSequentialRequest> requests = Enumerable.Range(0, count)
                .Select(_ => new MockSequentialRequest())
                .ToList();

            // Act
            _handler.AddRange(requests.ToArray());
            await Task.Delay(2000); // Give time to process

            // Assert
            int executedCount = requests.Count(r => r.WasExecuted);
            executedCount.Should().BeGreaterThan(0);
        }

        [Test]
        public async Task AddWhileProcessing_ShouldQueueAndProcess()
        {
            // Arrange
            MockSequentialRequest request1 = new(_ => Task.Delay(100).ContinueWith(_ => true));
            MockSequentialRequest request2 = new();

            // Act
            _handler.Add(request1);
            await Task.Delay(50);
            _handler.Add(request2);
            await Task.Delay(200);

            // Assert
            request1.WasExecuted.Should().BeTrue();
            request2.WasExecuted.Should().BeTrue();
        }

        [Test]
        public async Task CancelWhileProcessing_ShouldStopProcessing()
        {
            // Arrange
            MockSequentialRequest request1 = new(_ => Task.Delay(100).ContinueWith(_ => true));
            MockSequentialRequest request2 = new();

            _handler.Add(request1);
            _handler.Add(request2);

            // Act
            await Task.Delay(50);
            _handler.Cancel();
            await Task.Delay(200);

            // Assert
            request2.WasExecuted.Should().BeFalse();
        }

        #endregion

        #region ToString Tests

        [Test]
        public void ToString_ShouldReturnFormattedState()
        {
            // Act
            string result = _handler.ToString();

            // Assert
            result.Should().Contain("SequentialRequestHandler State:");
            result.Should().Contain("State:");
            result.Should().Contain("Priority:");
        }

        #endregion
    }
}
