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
        public async Task SubsequentRequest_OnCompletion_ShouldStartSubsequentRequest()
        {
            TestRequest subRequest = new(new()
            {
                AutoStart = false,
            }, 1000);
            TestRequest mainRequest = new(new()
            {
                SubsequentRequest = subRequest,
            }, 1000);

            await Task.Delay(500);
            Assert.AreEqual(RequestState.Paused, subRequest.State, "Subsequent request should be paused.");
            Assert.AreEqual(RequestState.Running, mainRequest.State, "Subsequent request should be paused.");

            await mainRequest.Task;
            await Task.Delay(500);

            // Assert
            Assert.AreEqual(RequestState.Running, subRequest.State, "Subsequent request should have started and completed.");
        }

        [TestMethod]
        public async Task SubsequentRequest_OnFailureWithDelay_ShouldCancelSubsequentRequest()
        {
            // Arrange
            TestRequest subRequest = new(new()
            {
                AutoStart = false,
            }, 1000);

            TestRequest mainRequest = new(new()
            {
                SubsequentRequest = subRequest,
            }, 1000);


            await Task.Delay(100);
            Assert.AreEqual(RequestState.Running, mainRequest.State, "Main request should be running.");
            // Act
            await Task.Delay(300);
            mainRequest.Cancel();
            await Task.Delay(100);

            // Assert
            Assert.AreEqual(RequestState.Cancelled, subRequest.State, "Subsequent request should have been canceled on main request failure.");
        }


        [TestMethod]
        public async Task SubsequentRequest_AlreadyRunning_ShouldNotRestart()
        {
            // Arrange
            TestRequest subRequest = new(new()
            {
                AutoStart = true,
            }, 1000);

            await Task.Delay(200);

            TestRequest mainRequest = new(new()
            {
                SubsequentRequest = subRequest,
            }, 100);

            Assert.AreEqual(RequestState.Running, subRequest.State, "Subsequent request should be running.");

            await mainRequest.Task;
            await Task.Delay(750);

            // Assert
            Assert.AreEqual(RequestState.Compleated, subRequest.State, "Subsequent request should have completed and no longer running.");
        }

        [TestMethod]
        public async Task CancelRequest_ShouldChangeStateToCancelled()
        {
            // Arrange
            TestRequest request = new();

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
            RequestHandler handler = new(0) { StaticDegreeOfParallelism = 1 };
            List<float> completedPriorities = new();
            Random random = new();

            _ = new TestRequest(new()
            {
                Handler = handler,
                Priority = RequestPriority.Normal,
            }, 1000);

            for (int i = 0; i < 10; i++)
            {
                float priority = (float)random.NextDouble() + random.Next(0, 3); // Generate a random float between 0 and 1

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
            TestRequest request = new();

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
            TestRequest request = new();

            // Act
            request.Pause();

            // Assert
            Assert.AreEqual(RequestState.Paused, request.State);
        }

        [TestMethod]
        public void StartRequestAfterPause_ShouldChangeStateToIdle()
        {
            // Arrange
            TestRequest request = new();
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
            TestRequest request = new(new RequestOptions<bool, bool>() { DeployDelay = TimeSpan.FromSeconds(2) });

            // Act
            request.Start();

            // Assert
            Assert.AreEqual(RequestState.Waiting, request.State);
        }

        [TestMethod]
        public async Task RequestWithCancellation_ShouldCancel()
        {
            // Arrange
            CancellationTokenSource cts = new();
            RequestOptions<bool, bool> options = new()
            { CancellationToken = cts.Token };
            TestRequest request = new(options);
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