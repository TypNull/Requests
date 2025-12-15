namespace UnitTest
{
    /// <summary>
    /// Comprehensive test suite for the ProgressableContainer class.
    /// Tests are organized by functionality area with AAA pattern (Arrange, Act, Assert).
    /// </summary>
    [TestFixture]
    public class ProgressableContainerTests
    {
        private ProgressableContainer<MockProgressableRequest> _container = null!;

        [SetUp]
        public void SetUp()
        {
            _container = new ProgressableContainer<MockProgressableRequest>();
        }

        [TearDown]
        public void TearDown()
        {
            _container?.Dispose();
        }

        #region Test Helper Classes

        /// <summary>
        /// Mock implementation of IProgressableRequest for testing purposes.
        /// Provides full control over state transitions and progress reporting.
        /// </summary>
        private class MockProgressableRequest : IProgressableRequest
        {
            private TaskCompletionSource<bool> _tcs = new();

            public Task Task => _tcs.Task;
            public RequestState State { get; private set; } = RequestState.Idle;
            public event EventHandler<RequestState>? StateChanged;
            public RequestPriority Priority { get; set; } = RequestPriority.Normal;
            public AggregateException? Exception { get; private set; }
            public IRequest? SubsequentRequest { get; private set; }
            public Progress<float> Progress { get; } = new Progress<float>();

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

            public void Complete()
            {
                SetState(RequestState.Completed);
                _tcs.TrySetResult(true);
            }

            public void Fail(Exception ex)
            {
                Exception = new AggregateException(ex);
                SetState(RequestState.Failed);
                _tcs.TrySetException(ex);
            }

            private void SetState(RequestState newState)
            {
                State = newState;
                StateChanged?.Invoke(this, newState);
            }

            public bool TrySetIdle()
            {
                if (State == RequestState.Idle) return true;
                SetState(RequestState.Idle);
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

            public void ReportProgress(float value)
            {
                ((IProgress<float>)Progress).Report(value);
            }
        }

        #endregion

        #region Constructor Tests

        [Test]
        public void Constructor_Default_ShouldInitializeEmpty()
        {
            // Arrange & Act
            using ProgressableContainer<MockProgressableRequest> container = new();

            // Assert
            container.Should().NotBeNull();
            container.Count.Should().Be(0);
            container.Progress.Should().NotBeNull();
            container.State.Should().Be(RequestState.Idle);
        }

        [Test]
        public void Constructor_WithSingleRequest_ShouldAddRequest()
        {
            // Arrange
            MockProgressableRequest request = new();

            // Act
            using ProgressableContainer<MockProgressableRequest> container = new(request);

            // Assert
            container.Count.Should().Be(1);
            container.Should().Contain(request);
        }

        [Test]
        public void Constructor_WithMultipleRequests_ShouldAddAllRequests()
        {
            // Arrange
            MockProgressableRequest[] requests = [new(), new(), new()];

            // Act
            using ProgressableContainer<MockProgressableRequest> container = new(requests);

            // Assert
            container.Count.Should().Be(3);
            container.Should().Contain(requests);
        }

        [Test]
        public void Constructor_WithNullArray_ShouldThrowArgumentNullException()
        {
            // Arrange
            MockProgressableRequest[]? requests = null;

            // Act
            Action act = () => _ = new ProgressableContainer<MockProgressableRequest>(requests!);

            // Assert
            act.Should().Throw<ArgumentNullException>();
        }

        #endregion

        #region Add/Remove Tests

        [Test]
        public void Add_SingleRequest_ShouldIncreaseCountAndAttachProgress()
        {
            // Arrange
            MockProgressableRequest request = new();
            bool progressReported = false;
            _container.Progress.ProgressChanged += (s, e) => progressReported = true;

            // Act
            _container.Add(request);
            request.ReportProgress(0.5f);

            // Assert
            _container.Count.Should().Be(1);
            _container.Should().Contain(request);
            progressReported.Should().BeTrue();
        }

        [Test]
        public void Add_NullRequest_ShouldThrowArgumentNullException()
        {
            // Arrange
            MockProgressableRequest? request = null;

            // Act
            Action act = () => _container.Add(request!);

            // Assert
            act.Should().Throw<ArgumentNullException>();
        }

        [Test]
        public void AddRange_MultipleRequests_ShouldAddAllAndAttachProgress()
        {
            // Arrange
            MockProgressableRequest[] requests = [new(), new(), new()];
            List<float> progressValues = [];
            _container.Progress.ProgressChanged += (s, e) => progressValues.Add(e);

            // Act
            _container.AddRange(requests);
            requests[0].ReportProgress(0.2f);
            requests[1].ReportProgress(0.5f);
            requests[2].ReportProgress(0.8f);

            // Assert
            _container.Count.Should().Be(3);
            _container.Should().Contain(requests);
            progressValues.Should().NotBeEmpty();
            progressValues.Should().HaveCountGreaterThan(0);
        }

        [Test]
        public void AddRange_EmptyArray_ShouldNotChangeCount()
        {
            // Arrange
            MockProgressableRequest[] requests = [];

            // Act
            _container.AddRange(requests);

            // Assert
            _container.Count.Should().Be(0);
        }

        [Test]
        public void Remove_ExistingRequest_ShouldRemoveAndDetachProgress()
        {
            // Arrange
            MockProgressableRequest request = new();
            _container.Add(request);
            bool progressReported = false;
            _container.Progress.ProgressChanged += (s, e) => progressReported = true;

            // Act
            _container.Remove(request);
            request.ReportProgress(0.5f);

            // Assert
            _container.Count.Should().Be(0);
            _container.Should().NotContain(request);
            progressReported.Should().BeFalse(); // Progress should be detached
        }

        [Test]
        public void Remove_NonExistingRequest_ShouldThrowInvalidOperationException()
        {
            // Arrange
            MockProgressableRequest request = new();

            // Act
            Action act = () => _container.Remove(request);

            // Assert
            act.Should().Throw<InvalidOperationException>();
            _container.Count.Should().Be(0);
        }

        [Test]
        public void Indexer_Get_ShouldReturnCorrectRequest()
        {
            // Arrange
            MockProgressableRequest request1 = new();
            MockProgressableRequest request2 = new();
            _container.AddRange(request1, request2);

            // Act & Assert
            _container[0].Should().Be(request1);
            _container[1].Should().Be(request2);
        }

        [Test]
        public void Indexer_Set_ShouldReplaceAndUpdateProgress()
        {
            // Arrange
            MockProgressableRequest originalRequest = new();
            MockProgressableRequest newRequest = new();
            _container.Add(originalRequest);
            List<float> progressValues = [];
            _container.Progress.ProgressChanged += (s, e) => progressValues.Add(e);

            // Act
            _container[0] = newRequest;
            newRequest.ReportProgress(0.8f);
            originalRequest.ReportProgress(0.2f); // Should not affect container

            // Assert
            _container[0].Should().Be(newRequest);
            _container.Should().NotContain(originalRequest);
            progressValues.Should().Contain(0.8f);
        }

        #endregion

        #region Progress Tracking Tests

        [Test]
        public void Progress_SingleRequest_ShouldReportExactValue()
        {
            // Arrange
            MockProgressableRequest request = new();
            _container.Add(request);
            float lastProgress = 0f;
            _container.Progress.ProgressChanged += (s, e) => lastProgress = e;

            // Act
            request.ReportProgress(0.75f);

            // Assert
            lastProgress.Should().Be(0.75f);
        }

        [Test]
        public void Progress_MultipleRequests_ShouldCalculateAverage()
        {
            // Arrange
            MockProgressableRequest request1 = new();
            MockProgressableRequest request2 = new();
            _container.AddRange(request1, request2);
            List<float> progressValues = [];
            _container.Progress.ProgressChanged += (s, e) => progressValues.Add(e);

            // Act
            request1.ReportProgress(0.2f); // Average: 0.1f (0.2 + 0.0) / 2
            request2.ReportProgress(0.6f); // Average: 0.4f (0.2 + 0.6) / 2

            // Assert
            progressValues.Should().Contain(0.1f);
            progressValues.Should().Contain(0.4f);
        }

        [Test]
        public void Progress_ThreeRequests_ShouldCalculateCorrectAverage()
        {
            // Arrange
            MockProgressableRequest request1 = new();
            MockProgressableRequest request2 = new();
            MockProgressableRequest request3 = new();
            _container.AddRange(request1, request2, request3);
            List<float> progressValues = [];
            _container.Progress.ProgressChanged += (s, e) => progressValues.Add(e);

            // Act
            request1.ReportProgress(0.3f); // Average: 0.1f (0.3 + 0.0 + 0.0) / 3
            request2.ReportProgress(0.6f); // Average: 0.3f (0.3 + 0.6 + 0.0) / 3
            request3.ReportProgress(0.9f); // Average: 0.6f (0.3 + 0.6 + 0.9) / 3

            // Assert
            progressValues.Should().Contain(0.1f);
            progressValues.Should().Contain(0.3f);
            progressValues.Should().Contain(0.6f);
        }

        [Test]
        public void Progress_NoRequests_ShouldNotReport()
        {
            // Arrange
            bool progressReported = false;
            _container.Progress.ProgressChanged += (s, e) => progressReported = true;

            // Act
            // No requests added, no progress should be reported

            // Assert
            progressReported.Should().BeFalse();
        }

        [Test]
        public void Progress_RequestRemoved_ShouldRecalculateAverage()
        {
            // Arrange
            MockProgressableRequest request1 = new();
            MockProgressableRequest request2 = new();
            _container.AddRange(request1, request2);
            request1.ReportProgress(0.4f);
            request2.ReportProgress(0.8f);

            float lastProgress = 0f;
            _container.Progress.ProgressChanged += (s, e) => lastProgress = e;

            // Act
            _container.Remove(request1);
            request2.ReportProgress(0.9f);

            // Assert
            lastProgress.Should().Be(0.9f); // Only request2 remains
        }

        #endregion

        #region State Management Tests

        [Test]
        public void Start_AllRequests_ShouldStartAll()
        {
            // Arrange
            MockProgressableRequest[] requests = [new(), new()];
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
            MockProgressableRequest[] requests = [new(), new()];
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
            MockProgressableRequest[] requests = [new(), new()];
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
            MockProgressableRequest[] requests = [new(), new()];
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
            MockProgressableRequest request = new();
            request.Start();
            _container.Add(request);

            // Act
            bool result = _container.TrySetIdle();

            // Assert
            result.Should().BeFalse(); // Was not already idle
            request.State.Should().Be(RequestState.Idle);
        }

        [Test]
        public void Dispose_AllRequests_ShouldDisposeAll()
        {
            // Arrange
            MockProgressableRequest[] requests = [new(), new()];
            _container.AddRange(requests);

            // Act
            _container.Dispose();

            // Assert
            requests.Should().AllSatisfy(r => r.HasCompleted().Should().BeTrue());
        }

        #endregion

        #region Static Methods Tests

        [Test]
        public void MergeContainers_TwoContainersWithRequests_ShouldCombineAll()
        {
            // Arrange
            using ProgressableContainer<MockProgressableRequest> container1 = new(new MockProgressableRequest(), new MockProgressableRequest());
            using ProgressableContainer<MockProgressableRequest> container2 = new(new MockProgressableRequest());

            // Act
            using ProgressableContainer<MockProgressableRequest> merged = ProgressableContainer<MockProgressableRequest>.MergeContainers(container1, container2);

            // Assert
            merged.Count.Should().Be(3);
            merged.Should().Contain(container1);
            merged.Should().Contain(container2);
        }

        [Test]
        public void MergeContainers_MultipleContainers_ShouldCombineAllRequests()
        {
            // Arrange
            using ProgressableContainer<MockProgressableRequest> container1 = new(new MockProgressableRequest(), new MockProgressableRequest());
            using ProgressableContainer<MockProgressableRequest> container2 = new(new MockProgressableRequest());
            using ProgressableContainer<MockProgressableRequest> container3 = new(new MockProgressableRequest(), new MockProgressableRequest(), new MockProgressableRequest());

            // Act
            using ProgressableContainer<MockProgressableRequest> merged = ProgressableContainer<MockProgressableRequest>.MergeContainers(container1, container2, container3);

            // Assert
            merged.Count.Should().Be(6);
        }

        [Test]
        public void MergeContainers_EmptyContainers_ShouldReturnEmpty()
        {
            // Arrange
            using ProgressableContainer<MockProgressableRequest> container1 = new();
            using ProgressableContainer<MockProgressableRequest> container2 = new();

            // Act
            using ProgressableContainer<MockProgressableRequest> merged = ProgressableContainer<MockProgressableRequest>.MergeContainers(container1, container2);

            // Assert
            merged.Count.Should().Be(0);
        }

        [Test]
        public void MergeContainers_MixedEmptyAndNonEmpty_ShouldCombineNonEmpty()
        {
            // Arrange
            using ProgressableContainer<MockProgressableRequest> container1 = new(new MockProgressableRequest());
            using ProgressableContainer<MockProgressableRequest> container2 = new();
            using ProgressableContainer<MockProgressableRequest> container3 = new(new MockProgressableRequest());

            // Act
            using ProgressableContainer<MockProgressableRequest> merged = ProgressableContainer<MockProgressableRequest>.MergeContainers(container1, container2, container3);

            // Assert
            merged.Count.Should().Be(2);
        }

        #endregion

        #region Enumeration Tests

        [Test]
        public void GetEnumerator_WithRequests_ShouldEnumerateAll()
        {
            // Arrange
            MockProgressableRequest[] requests = [new(), new(), new()];
            _container.AddRange(requests);

            // Act
            List<MockProgressableRequest> enumerated = _container.ToList();

            // Assert
            enumerated.Should().HaveCount(3);
            enumerated.Should().Contain(requests);
        }

        [Test]
        public void GetEnumerator_Empty_ShouldEnumerateNone()
        {
            // Act
            List<MockProgressableRequest> enumerated = _container.ToList();

            // Assert
            enumerated.Should().BeEmpty();
        }

        #endregion

        #region Task Management Tests

        [Test]
        public async Task Task_AllRequestsCompleted_ShouldComplete()
        {
            // Arrange
            MockProgressableRequest[] requests = [new(), new()];
            _container.AddRange(requests);

            // Act
            requests[0].Complete();
            requests[1].Complete();
            await _container.Task;

            // Assert
            _container.Task.IsCompleted.Should().BeTrue();
        }

        [Test]
        public void HasCompleted_AllCompleted_ShouldReturnTrue()
        {
            // Arrange
            MockProgressableRequest[] requests = [new(), new()];
            _container.AddRange(requests);
            requests[0].Complete();
            requests[1].Complete();

            // Act
            bool result = _container.HasCompleted();

            // Assert
            result.Should().BeTrue();
        }

        [Test]
        public void HasCompleted_SomeRunning_ShouldReturnFalse()
        {
            // Arrange
            MockProgressableRequest[] requests = [new(), new()];
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
            result.Should().BeTrue();
        }

        #endregion

        #region StateChanged Event Tests

        [Test]
        public void StateChanged_RequestStateChanges_ShouldFireEvent()
        {
            // Arrange
            MockProgressableRequest request = new();
            _container.Add(request);
            List<RequestState> stateChanges = [];
            _container.StateChanged += (s, state) => stateChanges.Add(state);

            // Act
            request.Start();

            // Assert
            stateChanges.Should().NotBeEmpty();
        }

        [Test]
        public void StateChanged_MultipleRequests_ShouldFireForEach()
        {
            // Arrange
            MockProgressableRequest[] requests = [new(), new()];
            _container.AddRange(requests);
            int eventCount = 0;
            _container.StateChanged += (s, state) => eventCount++;

            // Act
            _container.Start();

            // Assert
            eventCount.Should().BeGreaterThan(0);
        }

        #endregion

        #region Edge Cases and Stress Tests

        [Test]
        public void Add_ManyRequests_ShouldHandleAllCorrectly()
        {
            // Arrange
            const int count = 100;
            MockProgressableRequest[] requests = Enumerable.Range(0, count)
                .Select(_ => new MockProgressableRequest())
                .ToArray();

            // Act
            _container.AddRange(requests);

            // Assert
            _container.Count.Should().Be(count);
            _container.Should().Contain(requests);
        }

        [Test]
        public void Progress_ManyRequests_ShouldCalculateCorrectAverage()
        {
            // Arrange
            const int count = 50;
            MockProgressableRequest[] requests = Enumerable.Range(0, count)
                .Select(_ => new MockProgressableRequest())
                .ToArray();
            _container.AddRange(requests);

            float lastProgress = 0f;
            _container.Progress.ProgressChanged += (s, e) => lastProgress = e;

            // Act
            foreach (MockProgressableRequest request in requests)
            {
                request.ReportProgress(1.0f);
            }

            // Assert
            lastProgress.Should().BeApproximately(1.0f, 0.01f);
        }

        [Test]
        public void AddRemove_Interleaved_ShouldMaintainCorrectState()
        {
            // Arrange
            MockProgressableRequest request1 = new();
            MockProgressableRequest request2 = new();
            MockProgressableRequest request3 = new();

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

        #endregion
    }
}
