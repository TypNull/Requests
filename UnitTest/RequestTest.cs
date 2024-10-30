using Requests;
using Requests.Options;

namespace UnitTest
{
    [TestClass]
    public class RequestTests
    {
        private class TestRequest : Request<RequestOptions<bool, bool>, bool, bool>
        {
            public TestRequest(RequestOptions<bool, bool>? options = null) : base(options) { }

            protected override Task<RequestReturn> RunRequestAsync()
            {
                return Task.FromResult(new RequestReturn(true, true, false));
            }
        }

        [TestMethod]
        public async Task CancelRequest_ShouldChangeStateToCancelled()
        {
            // Arrange
            var request = new TestRequest();

            // Act
            request.Cancel();
            try
            {
                await request.Task;
            }
            catch (Exception)
            {
            }

            // Assert
            Assert.AreEqual(RequestState.Cancelled, request.State);
        }

        [TestMethod]
        public async Task DisposeRequest_ShouldCancelAndDispose()
        {
            // Arrange
            var request = new TestRequest();

            // Act
            request.Dispose();
            try
            {
                await request.Task;
            }
            catch (Exception)
            {
            }

            Assert.IsTrue(request.Task.IsCanceled);
        }

        [TestMethod]
        public void PauseRequest_ShouldChangeStateToPaused()
        {
            // Arrange
            var request = new TestRequest();

            // Act
            request.Pause();

            // Assert
            Assert.AreEqual(RequestState.Paused, request.State);
        }

        [TestMethod]
        public void StartRequestAfterPause_ShouldChangeStateToIdle()
        {
            // Arrange
            var request = new TestRequest();
            request.Pause();

            // Act
            request.Start();

            // Assert
            Assert.AreEqual(RequestState.Idle, request.State);
        }

        [TestMethod]
        public void RequestWithDeployDelay_ShouldChangeStateToWaiting()
        {
            // Arrange
            var request = new TestRequest
            {
                DeployDelay = TimeSpan.FromSeconds(2)
            };

            // Act
            request.Start();

            // Assert
            Assert.AreEqual(RequestState.Waiting, request.State);
        }

        [TestMethod]
        public async Task RequestWithCancellation_ShouldCancel()
        {
            // Arrange
            var cts = new CancellationTokenSource();
            var options = new RequestOptions<bool, bool> { CancellationToken = cts.Token };
            var request = new TestRequest(options);
            // Act
            cts.Cancel();
            try
            {
                await request.Task;
            }
            catch (Exception)
            {
            }

            // Assert
            Assert.AreEqual(RequestState.Cancelled, request.State);
        }
    }
}