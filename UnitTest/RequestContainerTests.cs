namespace UnitTest
{
    [TestFixture]
    public class RequestContainerTests
    {
        private class MockRequest : IRequest
        {
            public Task Task => Task.CompletedTask;
            public RequestState State
            {
                get => _state; private set
                {
                    _state = value;
                    StateChanged?.Invoke(this, _state);
                }
            }
            public RequestState _state = RequestState.Idle;
            public event EventHandler<RequestState>? StateChanged;
            public RequestPriority Priority => RequestPriority.Normal;
            public AggregateException? Exception => null;
            public IRequest? SubsequentRequest => null;

            public void Cancel() => State = RequestState.Cancelled;
            public void Dispose() => State = RequestState.Cancelled;
            public void Pause() => State = RequestState.Paused;
            public void Start() => State = RequestState.Running;
            public bool TrySetIdle()
            {
                if (State == RequestState.Idle) return true;
                State = RequestState.Idle;
                return false;
            }

            public Task StartRequestAsync() => Task.CompletedTask;
            public bool HasCompleted() => State is RequestState.Completed or RequestState.Failed or RequestState.Cancelled;
            public bool TrySetSubsequentRequest(IRequest request) => false;
            public void Wait() => Task.Wait();
        }

        [Test]
        public void Add_SingleRequest_ShouldIncreaseCount()
        {
            // Arrange & Act
            RequestContainer<MockRequest> container = new() { new() };

            // Assert
            container.Count.Should().Be(1);
        }

        [Test]
        public void AddRange_MultipleRequests_ShouldIncreaseCount()
        {
            // Arrange
            RequestContainer<MockRequest> container = new();

            // Act
            container.AddRange(new MockRequest(), new MockRequest());

            // Assert
            container.Count.Should().Be(2);
        }

        [Test]
        public void GetEnumerator_WithRequests_ShouldEnumerateAll()
        {
            // Arrange
            RequestContainer<MockRequest> container = new();
            container.AddRange(new MockRequest(), new MockRequest());

            // Act
            IEnumerator<MockRequest> enumerator = container.GetEnumerator();
            int count = 0;
            while (enumerator.MoveNext())
                count++;

            // Assert
            count.Should().Be(2);
        }

        [Test]
        public void TrySetIdle_WithIdleRequests_ShouldSucceed()
        {
            // Arrange
            RequestContainer<MockRequest> container = new();
            container.AddRange(new MockRequest(), new MockRequest());

            // Act
            bool result = container.TrySetIdle();

            // Assert
            result.Should().BeTrue();
            container.Should().AllSatisfy(r => r.State.Should().Be(RequestState.Idle));
        }

        [Test]
        public void Start_AllRequests_ShouldStartAll()
        {
            // Arrange
            RequestContainer<MockRequest> container = new();
            container.AddRange(new MockRequest(), new MockRequest());

            // Act
            container.Start();

            // Assert
            container.Should().AllSatisfy(r => r.State.Should().Be(RequestState.Running));
        }

        [Test]
        public void Cancel_AllRequests_ShouldCancelAll()
        {
            // Arrange
            RequestContainer<MockRequest> container = new();
            container.AddRange(new MockRequest(), new MockRequest());

            // Act
            container.Cancel();

            // Assert
            container.Should().AllSatisfy(r => r.State.Should().Be(RequestState.Cancelled));
        }

        [Test]
        public void Pause_AllRequests_ShouldPauseAll()
        {
            // Arrange
            RequestContainer<MockRequest> container = new();
            container.AddRange(new MockRequest(), new MockRequest());

            // Act
            container.Pause();

            // Assert
            container.Should().AllSatisfy(r => r.State.Should().Be(RequestState.Paused));
        }

        [Test]
        public void Dispose_AllRequests_ShouldDisposeAll()
        {
            // Arrange
            RequestContainer<MockRequest> container = new();
            container.AddRange(new MockRequest(), new MockRequest());

            // Act
            container.Dispose();

            // Assert
            container.Should().AllSatisfy(r => r.State.Should().Be(RequestState.Cancelled));
        }

        [Test]
        public void HasCompleted_CompletedRequests_ShouldReturnTrue()
        {
            // Arrange
            RequestContainer<MockRequest> container = new();
            MockRequest request = new();
            request.Cancel(); // Set to completed state
            container.Add(request);

            // Act & Assert
            container.HasCompleted().Should().BeTrue();
        }

        [Test]
        public void Task_AllRequests_ShouldCompleteWhenAllComplete()
        {
            // Arrange
            RequestContainer<MockRequest> container = new();
            container.AddRange(new MockRequest(), new MockRequest());

            // Act & Assert
            container.Task.Should().NotBeNull();
            container.Task.IsCompleted.Should().BeTrue(); // Mock requests complete immediately
        }
    }
}
