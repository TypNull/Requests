using Requests;
using Requests.Options;

namespace UnitTest
{
    [TestClass]
    public class RequestTests
    {
        private class TestRequest : Request<RequestOptions<bool, bool>, bool, bool>
        {
            private int _delay = 0;
            public TestRequest(RequestOptions<bool, bool>? options = null, int delay = 0) : base(options) { _delay = delay; AutoStart(); }

            protected override async Task<RequestReturn> RunRequestAsync()
            {
                if (_delay > 0)
                    await Task.Delay(_delay);
                return new RequestReturn(true, true, false);
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
        public async Task TestPriority()
        {
            var handler = new RequestHandler(0) { StaticDegreeOfParallelism = 1 };
            var completedPriorities = new List<float>();
            var random = new Random();

            _ = new TestRequest(new()
            {
                Handler = handler,
                Priority = RequestPriority.Normal,
            }, 1000);

            for (int i = 0; i < 10; i++)
            {
                var priority = (float)random.NextDouble() + random.Next(0, 3); // Generate a random float between 0 and 1

                _ = new TestRequest(new()
                {
                    Handler = handler,
                    RequestCompleated = (x, y) =>
                    {
                        Console.WriteLine(x!.Priority);
                        // Save the priority of the completed request
                        completedPriorities.Add(x.Priority);
                    },
                    Priority = priority,
                }, 100);
            }
            handler.RunRequests();
            // Wait for all requests to complete
            await Task.Delay(2000); // Adjust the delay as needed to ensure all requests are processed
            // Final assertion to check the order of completed priorities
            Assert.AreEqual(completedPriorities.Max(), completedPriorities.Last(), "The lowest priority value was not called first.");
            Assert.AreEqual(completedPriorities.Min(), completedPriorities.First(), "The highest priority value was not called last.");
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
            var request = new TestRequest(new RequestOptions<bool, bool>() { DeployDelay = TimeSpan.FromSeconds(2) });

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