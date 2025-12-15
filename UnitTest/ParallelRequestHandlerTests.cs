namespace UnitTest
{
    /// <summary>
    /// Test suite for the RequestHandler class using only public API.
    /// </summary>
    [TestFixture]
    public class ParallelRequestHandlerTests
    {
        private ParallelRequestHandler _handler = null!;

        [SetUp]
        public void SetUp()
        {
            _handler = new ParallelRequestHandler();
        }

        [TearDown]
        public void TearDown()
        {
            _handler?.Dispose();
        }

        #region Constructor Tests

        [Test]
        public void Constructor_Default_ShouldInitializeCorrectly()
        {
            // Act & Assert
            _handler.Should().NotBeNull();
            _handler.State.Should().Be(RequestState.Idle);
            _handler.Count.Should().Be(0);
        }

        #endregion

        #region Basic Properties Tests

        [Test]
        public void Properties_InitialState_ShouldHaveCorrectValues()
        {
            // Act & Assert
            _handler.State.Should().Be(RequestState.Idle);
            _handler.Count.Should().Be(0);
            _handler.Priority.Should().Be(RequestPriority.High);
            _handler.Task.Should().NotBeNull();
        }

        [Test]
        public void Count_EmptyHandler_ShouldBeZero()
        {
            // Act & Assert
            _handler.Count.Should().Be(0);
        }

        #endregion

        #region State Management Tests

        [Test]
        public void Start_PausedHandler_ShouldTransitionToIdle()
        {
            // Act
            _handler.Start();

            // Assert
            _handler.State.Should().Be(RequestState.Idle);
        }

        [Test]
        public void Pause_IdleHandler_ShouldTransitionToPaused()
        {
            // Arrange
            _handler.Start();

            // Act
            _handler.Pause();

            // Assert
            _handler.State.Should().Be(RequestState.Paused);
        }

        [Test]
        public void TrySetIdle_AllIdleRequests_ShouldReturnTrue()
        {
            // Arrange
            _handler.Pause();
            _handler.AddRange(CreateTestRequest(autoStart: false), CreateTestRequest(autoStart: false));

            // Act
            bool result = _handler.TrySetIdle();

            // Assert
            result.Should().BeTrue();
        }


        [Test]
        public void Cancel_Handler_ShouldTransitionToCancelled()
        {
            // Act
            _handler.Cancel();

            // Assert
            _handler.State.Should().Be(RequestState.Cancelled);
        }

        #endregion

        #region Request Management Tests

        [Test]
        public void Add_SingleRequest_ShouldIncreaseCount()
        {
            // Arrange
            _handler.Pause(); // Pause to prevent immediate execution
            OwnRequest request = CreateTestRequest(autoStart: false);

            // Act
            _handler.Add(request);

            // Assert
            _handler.Count.Should().Be(1);

            // Clean up
            request.Dispose();
        }

        [Test]
        public void AddRange_MultipleRequests_ShouldIncreaseCount()
        {
            // Arrange
            OwnRequest[] requests = [CreateTestRequest(autoStart: false), CreateTestRequest(autoStart: false)];

            // Act
            _handler.AddRange(requests);

            // Assert
            _handler.Count.Should().Be(2);

            // Clean up
            foreach (OwnRequest? request in requests)
                request.Dispose();
        }

        [Test]
        public void Remove_ExistingRequest_ShouldDecreaseCount()
        {
            // Arrange
            _handler.Pause(); // Pause to prevent immediate execution
            OwnRequest request = CreateTestRequest(autoStart: false);
            _handler.Add(request);

            // Act
            _handler.Remove(request);

            // Assert
            _handler.Count.Should().Be(0);

            // Clean up
            request.Dispose();
        }

        [Test]
        public void Remove_NonExistingRequest_ShouldReturnFalse()
        {
            // Arrange
            OwnRequest request = CreateTestRequest(autoStart: false);

            // Act & Assert
            // Remove throws InvalidOperationException when request doesn't exist
            Action act = () => _handler.Remove(request);
            act.Should().Throw<InvalidOperationException>();
            _handler.Count.Should().Be(0);

            // Clean up
            request.Dispose();
        }

        #endregion

        #region Execution Tests

        [Test]
        public async Task RunRequests_WithSingleRequest_ShouldExecuteRequest()
        {
            // Arrange
            OwnRequest request = CreateTestRequest();

            // Act
            _handler.Add(request);
            await request.Task;

            // Assert
            request.State.Should().Be(RequestState.Completed);

            // Clean up
            request.Dispose();
        }

        [Test]
        public async Task RunRequests_WithMultipleRequests_ShouldExecuteAll()
        {
            // Arrange
            OwnRequest[] requests = [CreateTestRequest(), CreateTestRequest()];

            // Act
            _handler.AddRange(requests);
            await Task.WhenAll(requests.Select(r => r.Task));

            // Assert
            foreach (OwnRequest? request in requests)
            {
                request.State.Should().Be(RequestState.Completed);
            }

            // Clean up
            foreach (OwnRequest? request in requests)
                request.Dispose();
        }

        #endregion

        #region State Events Tests

        [Test]
        public void StateChanged_WhenStateChanges_ShouldFireEvent()
        {
            // Arrange
            List<RequestState> stateChanges = [];
            _handler.StateChanged += (sender, state) => stateChanges.Add(state);

            // Act
            _handler.Pause();

            // Assert
            stateChanges.Should().NotBeEmpty();
            stateChanges.Should().Contain(RequestState.Paused);
        }

        #endregion

        #region Enumeration Tests

        [Test]
        public void Enumerate_WithRequests_ShouldReturnAllRequests()
        {
            // Arrange
            _handler.Pause();
            OwnRequest[] requests = [CreateTestRequest(autoStart: false), CreateTestRequest(autoStart: false)];
            _handler.AddRange(requests);

            // Act
            List<IRequest> enumerated = [.. _handler];

            // Assert
            enumerated.Should().HaveCount(2);
            enumerated.Should().Contain(requests);

            // Clean up
            foreach (OwnRequest? request in requests)
                request.Dispose();
        }

        #endregion

        #region Lifecycle Tests

        [Test]
        public void Dispose_Handler_ShouldComplete()
        {
            // Act
            _handler.Dispose();

            // Assert - should not throw
            _handler.State.Should().Be(RequestState.Cancelled);
        }

        [Test]
        public void HasCompleted_DisposedHandler_ShouldReturnTrue()
        {
            // Arrange
            _handler.Dispose();

            // Act & Assert
            _handler.HasCompleted().Should().BeTrue();
        }

        [Test]
        public void HasCompleted_CancelledHandlerWithRequests_ShouldReturnFalse()
        {
            // Arrange
            _handler.Pause(); // Pause to keep request in queue
            OwnRequest request = CreateTestRequest(autoStart: false);
            _handler.Add(request);
            _handler.Cancel();

            // Act & Assert
            _handler.HasCompleted().Should().BeFalse();

            // Clean up
            request.Dispose();
        }

        [Test]
        public void HasCompleted_RunningHandler_ShouldReturnFalse()
        {
            // Act & Assert
            _handler.HasCompleted().Should().BeFalse();
        }

        #endregion

        #region Helper Methods

        private OwnRequest CreateTestRequest(bool autoStart = true)
        {
            return new OwnRequest(async (token) =>
            {
                await Task.Delay(10, token);
                return true;
            }, new RequestOptions<object, object>
            {
                Handler = _handler,  // Use the test's handler, NOT the shared static one!
                AutoStart = autoStart
            });
        }

        #endregion
    }
}
