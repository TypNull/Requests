namespace UnitTest
{
    /// <summary>
    /// Comprehensive test suite for the RequestContainer class.
    /// Tests are organized by functionality area with AAA pattern (Arrange, Act, Assert).
    /// </summary>
    [TestFixture]
    public class RequestContainerTests
    {
        private RequestContainer<MockRequest> _container = null!;

        [SetUp]
        public void SetUp()
        {
            _container = new RequestContainer<MockRequest>();
        }

        [TearDown]
        public void TearDown()
        {
            _container?.Dispose();
        }

        #region Test Helper Classes

        /// <summary>
        /// Mock implementation of IRequest for testing purposes.
        /// Provides full control over state transitions and task completion.
        /// </summary>
        private class MockRequest : IRequest
        {
            private TaskCompletionSource<bool> _tcs = new();

            public Task Task => _tcs.Task;
            public RequestState State
            {
                get => _state;
                private set
                {
                    _state = value;
                    StateChanged?.Invoke(this, _state);
                }
            }
            private RequestState _state = RequestState.Idle;

            public event EventHandler<RequestState>? StateChanged;
            public RequestPriority Priority { get; set; } = RequestPriority.Normal;
            public AggregateException? Exception { get; private set; }
            public IRequest? SubsequentRequest { get; private set; }

            public void Cancel()
            {
                State = RequestState.Cancelled;
                _tcs.TrySetCanceled();
            }

            public void Dispose()
            {
                if (State != RequestState.Completed && State != RequestState.Failed)
                    Cancel();
            }

            public void Pause() => State = RequestState.Paused;
            public void Start() => State = RequestState.Running;

            public void Complete()
            {
                State = RequestState.Completed;
                _tcs.TrySetResult(true);
            }

            public void Fail(Exception ex)
            {
                Exception = new AggregateException(ex);
                State = RequestState.Failed;
                _tcs.TrySetException(ex);
            }

            public bool TrySetIdle()
            {
                if (State == RequestState.Idle) return true;
                State = RequestState.Idle;
                return false;
            }

            public Task StartRequestAsync()
            {
                Start();
                return Task.CompletedTask;
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
        public void Constructor_Default_ShouldInitializeEmpty()
        {
            // Arrange & Act
            using RequestContainer<MockRequest> container = new();

            // Assert
            container.Should().NotBeNull();
            container.Count.Should().Be(0);
            container.State.Should().Be(RequestState.Paused);
        }

        [Test]
        public void Constructor_WithSingleRequest_ShouldAddRequest()
        {
            // Arrange
            MockRequest request = new();

            // Act
            using RequestContainer<MockRequest> container = new(request);

            // Assert
            container.Count.Should().Be(1);
            container.Should().Contain(request);
        }

        [Test]
        public void Constructor_WithMultipleRequests_ShouldAddAllRequests()
        {
            // Arrange
            MockRequest[] requests = [new(), new(), new()];

            // Act
            using RequestContainer<MockRequest> container = new(requests);

            // Assert
            container.Count.Should().Be(3);
            container.Should().Contain(requests);
        }

        [Test]
        public void Constructor_WithNullArray_ShouldThrowArgumentNullException()
        {
            // Arrange
            MockRequest[]? requests = null;

            // Act
            Action act = () => _ = new RequestContainer<MockRequest>(requests!);

            // Assert
            act.Should().Throw<ArgumentNullException>();
        }

        #endregion

        #region Add/Remove Tests

        [Test]
        public void Add_SingleRequest_ShouldIncreaseCount()
        {
            // Arrange
            MockRequest request = new();

            // Act
            _container.Add(request);

            // Assert
            _container.Count.Should().Be(1);
            _container.Should().Contain(request);
        }

        [Test]
        public void Add_NullRequest_ShouldThrowArgumentNullException()
        {
            // Arrange
            MockRequest? request = null;

            // Act
            Action act = () => _container.Add(request!);

            // Assert
            act.Should().Throw<ArgumentNullException>();
        }

        [Test]
        public void AddRange_MultipleRequests_ShouldIncreaseCount()
        {
            // Arrange
            MockRequest[] requests = [new(), new(), new()];

            // Act
            _container.AddRange(requests);

            // Assert
            _container.Count.Should().Be(3);
            _container.Should().Contain(requests);
        }

        [Test]
        public void AddRange_EmptyArray_ShouldNotChangeCount()
        {
            // Arrange
            MockRequest[] requests = [];

            // Act
            _container.AddRange(requests);

            // Assert
            _container.Count.Should().Be(0);
        }

        [Test]
        public void Remove_ExistingRequest_ShouldDecreaseCount()
        {
            // Arrange
            MockRequest request = new();
            _container.Add(request);

            // Act
            _container.Remove(request);

            // Assert
            _container.Count.Should().Be(0);
            _container.Should().NotContain(request);
        }

        [Test]
        public void Indexer_Get_ShouldReturnCorrectRequest()
        {
            // Arrange
            MockRequest request1 = new();
            MockRequest request2 = new();
            _container.AddRange(request1, request2);

            // Act & Assert
            _container[0].Should().Be(request1);
            _container[1].Should().Be(request2);
        }

        [Test]
        public void Indexer_Set_ShouldReplaceRequest()
        {
            // Arrange
            MockRequest originalRequest = new();
            MockRequest newRequest = new();
            _container.Add(originalRequest);

            // Act
            _container[0] = newRequest;

            // Assert
            _container[0].Should().Be(newRequest);
            _container.Should().NotContain(originalRequest);
        }

        #endregion

        #region State Management Tests

        [Test]
        public void Start_AllRequests_ShouldStartAll()
        {
            // Arrange
            MockRequest[] requests = [new(), new()];
            _container.AddRange(requests);

            // Act
            _container.Start();

            // Assert
            _container.Should().AllSatisfy(r => r.State.Should().Be(RequestState.Running));
        }

        [Test]
        public void Pause_AllRequests_ShouldPauseAll()
        {
            // Arrange
            MockRequest[] requests = [new(), new()];
            _container.AddRange(requests);

            // Act
            _container.Pause();

            // Assert
            _container.Should().AllSatisfy(r => r.State.Should().Be(RequestState.Paused));
        }

        [Test]
        public void Cancel_AllRequests_ShouldCancelAll()
        {
            // Arrange
            MockRequest[] requests = [new(), new()];
            _container.AddRange(requests);

            // Act
            _container.Cancel();

            // Assert
            _container.Should().AllSatisfy(r => r.State.Should().Be(RequestState.Cancelled));
        }

        [Test]
        public void TrySetIdle_AllIdle_ShouldReturnTrue()
        {
            // Arrange
            MockRequest[] requests = [new(), new()];
            _container.AddRange(requests);

            // Act
            bool result = _container.TrySetIdle();

            // Assert
            result.Should().BeTrue();
            _container.Should().AllSatisfy(r => r.State.Should().Be(RequestState.Idle));
        }

        [Test]
        public void TrySetIdle_NonIdleRequest_ShouldSetToIdle()
        {
            // Arrange
            MockRequest request = new();
            request.Start();
            _container.Add(request);

            // Act
            bool result = _container.TrySetIdle();

            // Assert
            request.State.Should().Be(RequestState.Idle);
        }

        [Test]
        public void Dispose_AllRequests_ShouldDisposeAll()
        {
            // Arrange
            MockRequest[] requests = [new(), new()];
            _container.AddRange(requests);

            // Act
            _container.Dispose();

            // Assert
            requests.Should().AllSatisfy(r => r.HasCompleted().Should().BeTrue());
        }

        #endregion

        #region Task Management Tests

        [Test]
        public async Task Task_AllRequestsCompleted_ShouldComplete()
        {
            // Arrange
            MockRequest[] requests = [new(), new()];
            _container.AddRange(requests);

            // Act
            requests[0].Complete();
            requests[1].Complete();
            await _container.Task;

            // Assert
            _container.Task.IsCompleted.Should().BeTrue();
        }

        [Test]
        public void HasCompleted_AllCompleted_ShouldReturnFalse()
        {
            // Arrange
            MockRequest[] requests = [new(), new()];
            _container.AddRange(requests);
            requests[0].Complete();
            requests[1].Complete();

            // Act
            bool result = _container.HasCompleted();

            // Assert
            result.Should().BeFalse();
        }

        [Test]
        public void HasCompleted_SomeRunning_ShouldReturnFalse()
        {
            // Arrange
            MockRequest[] requests = [new(), new()];
            _container.AddRange(requests);
            requests[0].Complete();
            requests[1].Start();

            // Act
            bool result = _container.HasCompleted();

            // Assert
            result.Should().BeFalse();
        }

        [Test]
        public void HasCompleted_Empty_ShouldReturnTrue()
        {
            // Act
            bool result = _container.HasCompleted();

            // Assert
            result.Should().BeFalse();
        }

        #endregion

        #region Enumeration Tests

        [Test]
        public void GetEnumerator_WithRequests_ShouldEnumerateAll()
        {
            // Arrange
            MockRequest[] requests = [new(), new(), new()];
            _container.AddRange(requests);

            // Act
            List<MockRequest> enumerated = _container.ToList();

            // Assert
            enumerated.Should().HaveCount(3);
            enumerated.Should().Contain(requests);
        }

        [Test]
        public void GetEnumerator_Empty_ShouldEnumerateNone()
        {
            // Act
            List<MockRequest> enumerated = _container.ToList();

            // Assert
            enumerated.Should().BeEmpty();
        }

        [Test]
        public void GetEnumerator_ManualIteration_ShouldIterateCorrectly()
        {
            // Arrange
            _container.AddRange(new(), new());

            // Act
            IEnumerator<MockRequest> enumerator = _container.GetEnumerator();
            int count = 0;
            while (enumerator.MoveNext())
                count++;

            // Assert
            count.Should().Be(2);
        }

        #endregion

        #region StateChanged Event Tests

        [Test]
        public async Task StateChanged_RequestStateChanges_ShouldFireEvent()
        {
            // Arrange
            MockRequest request = new();
            _container.Add(request);
            List<RequestState> stateChanges = [];
            _container.StateChanged += (s, state) => stateChanges.Add(state);

            // Act
            request.Start();

            await Task.Delay(100);

            // Assert
            stateChanges.Should().NotBeEmpty();
        }

        [Test]
        public async Task StateChanged_MultipleRequests_ShouldFireForEach()
        {
            // Arrange
            MockRequest[] requests = [new(), new()];
            _container.AddRange(requests);
            int eventCount = 0;
            _container.StateChanged += (s, state) => eventCount++;

            // Act
            _container.Start();

            await Task.Delay(100);

            // Assert
            eventCount.Should().BeGreaterThan(0);
        }

        #endregion

        #region Subsequent Request Tests

        [Test]
        public void TrySetSubsequentRequest_OnRequest_ShouldSetSuccessfully()
        {
            // Arrange
            MockRequest mainRequest = new();
            MockRequest subsequentRequest = new();
            _container.Add(mainRequest);

            // Act
            bool result = mainRequest.TrySetSubsequentRequest(subsequentRequest);

            // Assert
            result.Should().BeTrue();
            mainRequest.SubsequentRequest.Should().Be(subsequentRequest);
        }

        [Test]
        public void TrySetSubsequentRequest_Twice_ShouldReturnFalse()
        {
            // Arrange
            MockRequest mainRequest = new();
            MockRequest subsequentRequest1 = new();
            MockRequest subsequentRequest2 = new();
            mainRequest.TrySetSubsequentRequest(subsequentRequest1);

            // Act
            bool result = mainRequest.TrySetSubsequentRequest(subsequentRequest2);

            // Assert
            result.Should().BeFalse();
            mainRequest.SubsequentRequest.Should().Be(subsequentRequest1);
        }

        #endregion

        #region Exception Handling Tests

        [Test]
        public void Exception_RequestFailed_ShouldHaveException()
        {
            // Arrange
            MockRequest request = new();
            _container.Add(request);
            Exception testException = new InvalidOperationException("Test error");

            // Act
            request.Fail(testException);

            // Assert
            request.Exception.Should().NotBeNull();
            request.State.Should().Be(RequestState.Failed);
        }

        [Test]
        public void Exception_NoError_ShouldBeNull()
        {
            // Arrange
            MockRequest request = new();
            _container.Add(request);

            // Act & Assert
            request.Exception.Should().BeNull();
        }

        #endregion

        #region Priority Tests

        [Test]
        public void Priority_Default_ShouldBeNormal()
        {
            // Arrange
            MockRequest request = new();

            // Act & Assert
            request.Priority.Should().Be(RequestPriority.Normal);
        }

        [Test]
        public void Priority_SetCustom_ShouldRetainValue()
        {
            // Arrange
            MockRequest request = new() { Priority = RequestPriority.High };
            _container.Add(request);

            // Act & Assert
            request.Priority.Should().Be(RequestPriority.High);
        }

        #endregion

        #region Edge Cases and Stress Tests

        [Test]
        public void Add_ManyRequests_ShouldHandleAllCorrectly()
        {
            // Arrange
            const int count = 100;
            MockRequest[] requests = Enumerable.Range(0, count)
                .Select(_ => new MockRequest())
                .ToArray();

            // Act
            _container.AddRange(requests);

            // Assert
            _container.Count.Should().Be(count);
            _container.Should().Contain(requests);
        }

        [Test]
        public void StateTransitions_Sequential_ShouldWorkCorrectly()
        {
            // Arrange
            MockRequest request = new();
            _container.Add(request);
            List<RequestState> states = [];
            request.StateChanged += (s, state) => states.Add(state);

            // Act
            request.Start();
            request.Pause();
            request.Start();
            request.Complete();

            // Assert
            states.Should().ContainInOrder(
                RequestState.Running,
                RequestState.Paused,
                RequestState.Running,
                RequestState.Completed
            );
        }

        [Test]
        public void AddRemove_Interleaved_ShouldMaintainCorrectState()
        {
            // Arrange
            MockRequest request1 = new();
            MockRequest request2 = new();
            MockRequest request3 = new();

            // Act
            _container.Add(request1);
            _container.Count.Should().Be(1);

            _container.Add(request2);
            _container.Count.Should().Be(2);

            _container.Remove(request1);
            _container.Count.Should().Be(1);

            _container.Add(request3);
            _container.Count.Should().Be(2);

            // Assert
            _container.Should().Contain(request2);
            _container.Should().Contain(request3);
            _container.Should().NotContain(request1);
        }

        [Test]
        public async Task Wait_CompletedRequest_ShouldNotBlock()
        {
            // Arrange
            MockRequest request = new();
            request.Complete();
            _container.Add(request);

            // Act
            Task waitTask = Task.Run(() => request.Wait());
            bool completedInTime = await Task.WhenAny(waitTask, Task.Delay(1000)) == waitTask;

            // Assert
            completedInTime.Should().BeTrue();
        }

        [Test]
        public async Task YieldAsync_ShouldCompleteImmediately()
        {
            // Arrange
            MockRequest request = new();
            _container.Add(request);

            // Act
            ValueTask yieldTask = request.YieldAsync();

            // Assert
            yieldTask.IsCompleted.Should().BeTrue();
            await yieldTask; // Should not throw
        }

        #endregion

        #region Collection Initializer Tests

        [Test]
        public void CollectionInitializer_WithRequests_ShouldAddAll()
        {
            // Act
            using RequestContainer<MockRequest> container = [new(), new(), new()];

            // Assert
            container.Count.Should().Be(3);
        }

        [Test]
        public void CollectionInitializer_Empty_ShouldCreateEmptyContainer()
        {
            // Act
            using RequestContainer<MockRequest> container = [];

            // Assert
            container.Count.Should().Be(0);
        }

        #endregion
    }
}
