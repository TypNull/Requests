namespace UnitTest
{
    /// <summary>
    /// Test suite for the ProgressableContainer class.
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

        #region Test Helper Class

        private class MockProgressableRequest : IProgressableRequest
        {
            public Task Task => Task.CompletedTask;
            public RequestState State { get; private set; } = RequestState.Idle;
            public event EventHandler<RequestState>? StateChanged;
            public RequestPriority Priority => RequestPriority.Normal;
            public AggregateException? Exception => null;
            public IRequest? SubsequentRequest => null;
            public Progress<float> Progress { get; } = new Progress<float>();

            public void Cancel() => SetState(RequestState.Cancelled);
            public void Dispose() => SetState(RequestState.Cancelled);
            public void Pause() => SetState(RequestState.Paused);
            public void Start() => SetState(RequestState.Running);

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

            public Task StartRequestAsync() => Task.CompletedTask;
            public bool HasCompleted() => State is RequestState.Completed or RequestState.Failed or RequestState.Cancelled;
            public bool TrySetSubsequentRequest(IRequest request) => false;
            public void Wait() => Task.Wait();

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
            // Act & Assert
            _container.Should().NotBeNull();
            _container.Count.Should().Be(0);
            _container.Progress.Should().NotBeNull();
        }

        [Test]
        public void Constructor_WithRequests_ShouldAddRequests()
        {
            // Arrange
            MockProgressableRequest[] requests = new[] { new MockProgressableRequest(), new MockProgressableRequest() };

            // Act
            ProgressableContainer<MockProgressableRequest> container = new(requests);

            // Assert
            container.Count.Should().Be(2);
            container.Should().Contain(requests);

            // Clean up
            container.Dispose();
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
        public void AddRange_MultipleRequests_ShouldAddAllAndAttachProgress()
        {
            // Arrange
            MockProgressableRequest[] requests = new[] { new MockProgressableRequest(), new MockProgressableRequest() };
            List<float> progressValues = new();
            _container.Progress.ProgressChanged += (s, e) => progressValues.Add(e);

            // Act
            _container.AddRange(requests);
            requests[0].ReportProgress(0.3f);
            requests[1].ReportProgress(0.7f);

            // Assert
            _container.Count.Should().Be(2);
            _container.Should().Contain(requests);
            progressValues.Should().NotBeEmpty();
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
        public void Indexer_SetItem_ShouldReplaceAndUpdateProgress()
        {
            // Arrange
            MockProgressableRequest originalRequest = new();
            MockProgressableRequest newRequest = new();
            _container.Add(originalRequest);
            List<float> progressValues = new();
            _container.Progress.ProgressChanged += (s, e) => progressValues.Add(e);

            // Act
            _container[0] = newRequest;
            newRequest.ReportProgress(0.8f);
            originalRequest.ReportProgress(0.2f); // Should not affect container

            // Assert
            _container[0].Should().Be(newRequest);
            _container.Should().NotContain(originalRequest);
            progressValues.Should().Contain(0.8f);
            progressValues.Count(v => v == 0.2f).Should().Be(0); // Original request progress detached
        }

        #endregion

        #region Progress Tracking Tests

        [Test]
        public void Progress_MultipleRequests_ShouldCalculateAverage()
        {
            // Arrange
            MockProgressableRequest request1 = new();
            MockProgressableRequest request2 = new();
            _container.AddRange(request1, request2);

            List<float> progressValues = new();
            _container.Progress.ProgressChanged += (s, e) => progressValues.Add(e);

            // Act
            request1.ReportProgress(0.2f); // Average: 0.1f (0.2 + 0.0) / 2
            request2.ReportProgress(0.6f); // Average: 0.4f (0.2 + 0.6) / 2

            // Assert
            progressValues.Should().Contain(0.1f);
            progressValues.Should().Contain(0.4f);
        }

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

        #endregion

        #region Static Methods Tests

        [Test]
        public void MergeContainers_MultipleContainers_ShouldCombineAllRequests()
        {
            // Arrange
            ProgressableContainer<MockProgressableRequest> container1 = new(new MockProgressableRequest(), new MockProgressableRequest());
            ProgressableContainer<MockProgressableRequest> container2 = new(new MockProgressableRequest());
            ProgressableContainer<MockProgressableRequest> container3 = new();

            // Act
            ProgressableContainer<MockProgressableRequest> merged = ProgressableContainer<MockProgressableRequest>.MergeContainers(container1, container2, container3);

            // Assert
            merged.Count.Should().Be(3);
            merged.Should().Contain(container1);
            merged.Should().Contain(container2);

            // Clean up
            container1.Dispose();
            container2.Dispose();
            container3.Dispose();
            merged.Dispose();
        }

        [Test]
        public void MergeContainers_EmptyContainers_ShouldReturnEmpty()
        {
            // Arrange
            ProgressableContainer<MockProgressableRequest> container1 = new();
            ProgressableContainer<MockProgressableRequest> container2 = new();

            // Act
            ProgressableContainer<MockProgressableRequest> merged = ProgressableContainer<MockProgressableRequest>.MergeContainers(container1, container2);

            // Assert
            merged.Count.Should().Be(0);

            // Clean up
            container1.Dispose();
            container2.Dispose();
            merged.Dispose();
        }

        #endregion

        #region Inheritance Tests

        [Test]
        public void InheritedMethods_StateManagement_ShouldWorkCorrectly()
        {
            // Arrange
            MockProgressableRequest[] requests = new[] { new MockProgressableRequest(), new MockProgressableRequest() };
            _container.AddRange(requests);

            // Act
            _container.Start();

            // Assert
            _container.Should().AllSatisfy(r => r.State.Should().Be(RequestState.Running));
        }

        [Test]
        public void InheritedMethods_CancelAll_ShouldCancelAllRequests()
        {
            // Arrange
            MockProgressableRequest[] requests = new[] { new MockProgressableRequest(), new MockProgressableRequest() };
            _container.AddRange(requests);

            // Act
            _container.Cancel();

            // Assert
            _container.Should().AllSatisfy(r => r.State.Should().Be(RequestState.Cancelled));
        }

        #endregion
    }
}
