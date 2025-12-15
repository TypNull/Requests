namespace UnitTest
{
    /// <summary>
    /// Simple test suite for the OwnRequest implementation.
    /// </summary>
    [TestFixture]
    public class OwnRequestTests
    {
        [Test]
        public async Task OwnRequest_SuccessfulExecution_ShouldComplete()
        {
            // Arrange
            bool executed = false;
            OwnRequest request = new(async (token) =>
            {
                await Task.Delay(10, token);
                executed = true;
                return true;
            });

            // Act
            await request.Task;

            // Assert
            executed.Should().BeTrue();
            request.State.Should().Be(RequestState.Completed);
        }

        [Test]
        public async Task OwnRequest_FailedExecution_ShouldFail()
        {
            // Arrange
            OwnRequest request = new(async (token) =>
            {
                await Task.Delay(10, token);
                return false;
            }, new() { NumberOfAttempts = 1 });

            // Act
            await request.Task;

            // Assert
            request.State.Should().Be(RequestState.Failed);
        }

        [Test]
        public async Task OwnRequest_WithException_ShouldHandleGracefully()
        {
            // Arrange
            OwnRequest request = new(async (token) =>
            {
                await Task.Delay(10, token);
                throw new InvalidOperationException("Test exception");
            });

            // Act
            await request.Task;

            // Assert
            request.State.Should().Be(RequestState.Failed);
            request.Exception.Should().NotBeNull();
        }

        [Test]
        public void OwnRequest_WithCancellation_ShouldCancel()
        {
            // Arrange
            RequestOptions<object, object> options = new()
            {
                AutoStart = false
            };
            OwnRequest request = new(async (token) =>
            {
                await Task.Delay(5000, token);
                return true;
            }, options);

            // Act
            request.Start();
            Task.Delay(50).ContinueWith(_ => request.Cancel());

            try
            {

                request.Task.Wait(2000).Should().BeTrue();
            }
            catch
            { }

            // Assert
            request.State.Should().Be(RequestState.Cancelled);
        }

        [Test]
        public void OwnRequest_BasicProperties_ShouldBeCorrect()
        {
            // Arrange & Act
            OwnRequest request = new((token) => Task.FromResult(true));

            // Assert
            request.Should().NotBeNull();
            request.Priority.Should().Be(RequestPriority.Normal);
            request.State.Should().BeOneOf(RequestState.Idle, RequestState.Running, RequestState.Completed);
        }
    }
}
