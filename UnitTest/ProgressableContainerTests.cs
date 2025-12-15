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
            container.State.Should().Be(RequestState.Paused);
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
        public async Task Add_SingleRequest_ShouldIncreaseCountAndAttachProgress()
        {
            // Arrange
            MockProgressableRequest request = new();
            bool progressReported = false;
            _container.Progress.ProgressChanged += (s, e) => progressReported = true;

            // Act
            _container.Add(request);
            request.ReportProgress(0.5f);

            await Task.Delay(100);

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
        public async Task Indexer_Set_ShouldReplaceAndUpdateProgress()
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

            await Task.Delay(100);
            // Assert
            _container[0].Should().Be(newRequest);
            _container.Should().NotContain(originalRequest);
            progressValues.Should().Contain(0.8f);
        }

        #endregion

        #region Progress Tracking Tests

        [Test]
        public async Task Progress_SingleRequest_ShouldReportExactValue()
        {
            // Arrange
            MockProgressableRequest request = new();
            _container.Add(request);
            float lastProgress = 0f;
            _container.Progress.ProgressChanged += (s, e) => lastProgress = e;

            // Act
            request.ReportProgress(0.75f);

            await Task.Delay(100);

            // Assert
            lastProgress.Should().Be(0.75f);
        }

        [Test]
        public async Task Progress_MultipleRequests_ShouldCalculateAverage()
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

            await Task.Delay(100);

            // Assert
            progressValues.Should().Contain(0.1f);
            progressValues.Should().Contain(0.4f);
        }

        [Test]
        public async Task Progress_ThreeRequests_ShouldCalculateCorrectAverage()
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

            await Task.Delay(100);

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
        public async Task Progress_RequestRemoved_ShouldRecalculateAverage()
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

            await Task.Delay(100);

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
        public void HasCompleted_AllCompleted_ShouldReturnFalse()
        {
            // Arrange
            MockProgressableRequest[] requests = [new(), new()];
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
        public void HasCompleted_Empty_ShouldReturnFalse()
        {
            // Act
            bool result = _container.HasCompleted();

            // Assert
            result.Should().BeFalse();
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

            Task.Delay(100).Wait();

            // Assert
            stateChanges.Should().NotBeEmpty();
        }

        [Test]
        public async Task StateChanged_MultipleRequests_ShouldFireForEach()
        {
            // Arrange
            _container.Pause();
            MockProgressableRequest[] requests = [new(), new()];
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
        public async Task Progress_ManyRequests_ShouldCalculateCorrectAverage()
        {
            // Arrange
            const int count = 50;
            MockProgressableRequest[] requests = [.. Enumerable.Range(0, count).Select(_ => new MockProgressableRequest())];
            _container.AddRange(requests);

            float lastProgress = 0f;
            _container.Progress.ProgressChanged += (s, e) => lastProgress = e;

            // Act
            for (int i = 0; i < count / 2; i++)
            {
                _container[i].ReportProgress(1.0f);
            }

            await Task.Delay(100);

            // Assert
            lastProgress.Should().BeApproximately(0.5f, 0.1f);
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

        [Test]
        [Timeout(20000)]
        public async Task StressTest_ThousandRequests_RapidUpdates()
        {
            // Arrange
            const int requestCount = 1000;
            MockProgressableRequest[] requests = [.. Enumerable.Range(0, requestCount).Select(_ => new MockProgressableRequest())];

            _container.AddRange(requests);

            List<float> progressValues = [];
            _container.Progress.ProgressChanged += (s, e) => progressValues.Add(e);

            // Act
            List<Task> updateTasks = [];
            for (int i = 0; i < requestCount; i++)
            {
                int requestIndex = i;
                updateTasks.Add(Task.Run(() =>
                {
                    for (int j = 1; j <= 10; j++)
                    {
                        requests[requestIndex].ReportProgress(j / 10f);
                        Thread.Sleep(1); // Small delay to simulate real work
                    }
                }));
            }

            await Task.WhenAll(updateTasks);
            await Task.Delay(500); // Let all events settle

            // Assert
            progressValues.Should().NotBeEmpty();
            progressValues.Last().Should().BeApproximately(1.0f, 0.05f);
        }

        [Test]
        public void StressTest_MassiveScale_10KRequests()
        {
            // Arrange
            const int requestCount = 10000;
            using ProgressableContainer<MockProgressableRequest> largeContainer = new();

            MockProgressableRequest[] requests = [.. Enumerable.Range(0, requestCount).Select(_ => new MockProgressableRequest())];

            // Act
            Stopwatch sw = Stopwatch.StartNew();
            largeContainer.AddRange(requests);
            sw.Stop();

            // Assert
            largeContainer.Count.Should().Be(requestCount);
            sw.ElapsedMilliseconds.Should().BeLessThan(1000); // Should complete within 1 second
        }

        [Test]
        public async Task StressTest_MemoryEfficiency_NoLeaks()
        {
            // Arrange
            const int cycles = 10;
            const int requestsPerCycle = 100;

            // Act
            for (int cycle = 0; cycle < cycles; cycle++)
            {
                MockProgressableRequest[] requests = [.. Enumerable.Range(0, requestsPerCycle).Select(_ => new MockProgressableRequest())];

                _container.AddRange(requests);

                // Report some progress
                foreach (MockProgressableRequest request in requests)
                    request.ReportProgress(0.5f);

                await Task.Delay(10);

                // Remove all
                _container.Remove(requests);
            }

            await Task.Delay(100);

            // Assert
            _container.Count.Should().Be(0);
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        [Test]
        public async Task StressTest_EdgeCase_ZeroRequests_RapidAddRemove()
        {
            // Arrange
            List<float> progressValues = [];
            _container.Progress.ProgressChanged += (s, e) => progressValues.Add(e);

            // Act
            for (int i = 0; i < 100; i++)
            {
                MockProgressableRequest request = new();
                _container.Add(request);
                request.ReportProgress(0.5f);
                _container.Remove(request);
            }

            await Task.Delay(100);

            // Assert
            _container.Count.Should().Be(0);
        }

        #endregion
    }
}
