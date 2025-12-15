namespace UnitTest
{
    /// <summary>
    /// Test suite for the Request base class using only public API.
    /// </summary>
    [TestFixture]
    public class RequestTests
    {
        private TestRequest _request = null!;
        private RequestOptions<string, Exception> _options = null!;

        [SetUp]
        public void SetUp()
        {
            _options = new RequestOptions<string, Exception>
            {
                NumberOfAttempts = 3,
                Priority = RequestPriority.High,
                DelayBetweenAttemps = TimeSpan.FromMilliseconds(10),
                AutoStart = false
            };
            _request = new TestRequest(_options);
        }

        [TearDown]
        public void TearDown()
        {
            _request?.Dispose();
        }

        #region Test Helper Class

        private class TestRequest : Request<RequestOptions<string, Exception>, string, Exception>
        {
            public bool ShouldSucceed { get; set; } = true;
            public bool ShouldThrow { get; set; } = false;
            public int ExecutionCount { get; private set; } = 0;

            public TestRequest(RequestOptions<string, Exception> options) : base(options)
            {
            }

            protected override async Task<RequestReturn> RunRequestAsync()
            {
                Debug.WriteLine("Run Work");
                ExecutionCount++;

                if (ShouldThrow)
                    throw new InvalidOperationException($"Test exception on attempt {ExecutionCount}");

                if (!ShouldSucceed)
                    return new RequestReturn { Successful = false, FailedReturn = new Exception($"Test failure on attempt {ExecutionCount}") };

                await Task.Delay(50); // Simulate work
                return new RequestReturn { Successful = true, CompletedReturn = $"Success on attempt {ExecutionCount}" };
            }
        }

        #endregion

        #region Basic Properties Tests

        [Test]
        public void Constructor_ValidOptions_ShouldInitializeCorrectly()
        {
            // Act & Assert
            _request.Should().NotBeNull();
            _request.State.Should().Be(RequestState.Paused);
            _request.Priority.Should().Be(RequestPriority.High);
        }

        [Test]
        public void Constructor_NullOptions_ShouldUseDefaults()
        {
            // Act
            TestRequest request = new(null);

            // Assert
            request.Should().NotBeNull();
            request.Priority.Should().Be(RequestPriority.Normal);

            // Clean up
            request.Dispose();
        }

        [Test]
        public void Properties_InitialState_ShouldHaveCorrectValues()
        {
            // Act & Assert
            _request.State.Should().Be(RequestState.Paused);
            _request.Priority.Should().Be(RequestPriority.High);
            _request.Task.Should().NotBeNull();
        }

        #endregion

        #region State Management Tests

        [Test]
        public void Start_PausedRequest_ShouldTransitionToIdle()
        {
            // Act
            _request.Start();

            // Assert
            _request.State.Should().Be(RequestState.Idle);
        }

        [Test]
        public void Pause_IdleRequest_ShouldTransitionToPaused()
        {
            // Arrange
            _request.Start();

            // Act
            _request.Pause();

            // Assert
            _request.State.Should().Be(RequestState.Paused);
        }

        [Test]
        public void Cancel_Request_ShouldTransitionToCancelled()
        {
            // Act
            _request.Cancel();

            // Assert
            _request.State.Should().Be(RequestState.Cancelled);
        }

        #endregion

        #region Execution Tests

        [Test]
        public async Task Task_SuccessfulRequest_ShouldComplete()
        {
            // Arrange
            _request.ShouldSucceed = true;

            // Act
            _request.Start();
            await _request.Task;

            // Assert
            _request.State.Should().Be(RequestState.Completed);
            _request.ExecutionCount.Should().Be(1);
        }

        [Test]
        public async Task Task_FailedRequest_ShouldFail()
        {
            // Arrange
            _request.ShouldSucceed = false;

            // Act
            _request.Start();
            await _request.Task;

            // Assert
            _request.State.Should().Be(RequestState.Failed);
            _request.ExecutionCount.Should().Be(_options.NumberOfAttempts);
        }

        [Test]
        public async Task Task_RequestWithException_ShouldFail()
        {
            // Arrange
            _request.ShouldThrow = true;

            // Act
            _request.Start();
            await _request.Task;
            Debug.WriteLine("Finished");
            
            // Assert
            _request.State.Should().Be(RequestState.Failed);
            _request.Exception.Should().NotBeNull();
            _request.ExecutionCount.Should().Be(_options.NumberOfAttempts, "Request should retry the specified number of times before failing");
        }

        #endregion

        #region Event Tests

        [Test]
        public async Task StateChanged_DuringExecution_ShouldFireEvents()
        {
            // Arrange
            List<RequestState> stateChanges = new();
            _request.StateChanged += (sender, state) => stateChanges.Add(state);

            // Act
            _request.Start();
            await _request.Task;

            // Assert
            stateChanges.Should().Contain(RequestState.Idle);
            stateChanges.Should().Contain(RequestState.Running);
            stateChanges.Should().ContainInOrder(RequestState.Idle, RequestState.Running);
        }

        #endregion

        #region Lifecycle Tests

        [Test]
        public void Dispose_Request_ShouldComplete()
        {
            // Act
            _request.Dispose();

            // Assert - should not throw
            _request.State.Should().BeOneOf(RequestState.Cancelled);
        }

        [Test]
        public void HasCompleted_CompletedRequest_ShouldReturnTrue()
        {
            // Arrange
            _request.Cancel();

            // Act & Assert
            _request.HasCompleted().Should().BeTrue();
        }

        [Test]
        public void HasCompleted_RunningRequest_ShouldReturnFalse()
        {
            // Act & Assert
            _request.HasCompleted().Should().BeFalse();
        }

        #endregion
    }
}
