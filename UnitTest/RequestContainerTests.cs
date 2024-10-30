using Requests;
using Requests.Options;
using System.Diagnostics;

namespace UnitTest
{
    [TestClass]
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
                    _state = value;
                    StateChanged?.Invoke(this, _state);
                }
            }
            public RequestState _state = RequestState.Idle;
            public event EventHandler<RequestState>? StateChanged;
            public RequestPriority Priority => RequestPriority.Normal;
            public AggregateException? Exception => null;

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
        }

        [TestMethod]
        public void TestAddAndLength()
        {
            var container = new RequestContainer<MockRequest> { new() };
            Assert.AreEqual(1, container.Length);
            Debug.WriteLine($"Length after adding one request: {container.Length}");
        }

        [TestMethod]
        public void TestAddRangeAndLength()
        {
            var container = new RequestContainer<MockRequest>();
            container.AddRange(new MockRequest(), new MockRequest());
            Assert.AreEqual(2, container.Length);
            Debug.WriteLine($"Length after adding two requests: {container.Length}");
        }

        [TestMethod]
        public void TestGetEnumerator()
        {
            var container = new RequestContainer<MockRequest>();
            container.AddRange(new MockRequest(), new MockRequest());
            var enumerator = container.GetEnumerator();
            int count = 0;
            while (enumerator.MoveNext())
                count++;
            Assert.AreEqual(2, count);
            Debug.WriteLine($"Enumerated {count} requests");
        }

        [TestMethod]
        public void TestSetIdleWhileFilling()
        {
            var container = new RequestContainer<MockRequest>();
            var request1 = new MockRequest();
            var request2 = new MockRequest();
            container.Add(request1);
            container.Add(request2);
            request1.Start();
            request2.Start();
            container.TrySetIdle();
            Assert.AreEqual(RequestState.Idle, container.State);
            Debug.WriteLine($"Container state after setting idle: {container.State}");
        }

        [TestMethod]
        public void TestPauseAndStart()
        {
            var container = new RequestContainer<MockRequest>();
            var request1 = new MockRequest();
            var request2 = new MockRequest();
            container.Add(request1);
            container.Add(request2);
            request1.Start();
            request2.Start();
            container.Pause();
            Assert.AreEqual(RequestState.Paused, container.State);
            Debug.WriteLine($"Container state after pausing: {container.State}");

            container.Start();
            Assert.AreEqual(RequestState.Running, container.State);
            Debug.WriteLine($"Container state after starting: {container.State}");
        }

        [TestMethod]
        public void TestOnStateChanged()
        {
            var container = new RequestContainer<MockRequest>();
            var request1 = new MockRequest();
            var request2 = new MockRequest();
            container.Add(request1);
            container.Add(request2);
            request1.Start();
            request2.Start();
            container.StateChanged += (sender, state) =>
            {
                Debug.WriteLine($"State changed to: {state}");
            };
            request1.Pause();
            request2.Pause();
            Assert.AreEqual(RequestState.Paused, container.State);
        }

        [TestMethod]
        public void TestCalculateState()
        {
            var container = new RequestContainer<MockRequest>();
            var request1 = new MockRequest();
            var request2 = new MockRequest();
            container.Add(request1);
            container.Add(request2);
            request1.Start();
            request2.Start();
            Assert.AreEqual(RequestState.Running, container.State);
            Debug.WriteLine($"Calculated state: {container.State}");
        }

        [TestMethod]
        public void TestMergeContainers()
        {
            var container1 = new RequestContainer<MockRequest>();
            var container2 = new RequestContainer<MockRequest>();
            container1.Add(new MockRequest());
            container2.Add(new MockRequest());
            var mergedContainer = RequestContainer<MockRequest>.MergeContainers(container1, container2);
            Assert.AreEqual(2, mergedContainer.Length);
            Debug.WriteLine($"Length of merged container: {mergedContainer.Length}");
        }

        [TestMethod]
        public void TestAccessItems()
        {
            var container = new RequestContainer<MockRequest>();
            var request1 = new MockRequest();
            var request2 = new MockRequest();
            container.Add(request1);
            container.Add(request2);
            Assert.AreEqual(request1, container[0]);
            Assert.AreEqual(request2, container[1]);
            Debug.WriteLine($"Accessed item at index 0: {container[0]}");
            Debug.WriteLine($"Accessed item at index 1: {container[1]}");
        }

        [TestMethod]
        public void TestMultithreadedReadingAndWriting()
        {
            var container = new RequestContainer<MockRequest>();
            for (int i = 0; i < 1000; i++)
            {
                container.Add(new MockRequest());
            }

            var tasks = new List<Task>();
            for (int i = 0; i < 1000; i++)
            {
                tasks.Add(Task.Run(() =>
                {
                    var request = container[i % container.Length];
                    Assert.IsNotNull(request);
                }));

                tasks.Add(Task.Run(() =>
                {
                    container[i % container.Length] = new MockRequest();
                }));
            }

            Task.WaitAll(tasks.ToArray());
            Assert.AreEqual(1000, container.Length);
            Debug.WriteLine($"Length after multithreaded reading and writing: {container.Length}");
        }

        [TestMethod]
        public void TestMultithreadedAddingAndRemoving()
        {
            var container = new RequestContainer<MockRequest>();
            var tasks = new List<Task>();

            for (int i = 0; i < 1000; i++)
            {
                tasks.Add(Task.Run(() => container.Add(new MockRequest())));
                tasks.Add(Task.Run(() =>
                {
                    if (container.Length > 0)
                    {
                        container.Remove(container[0]);
                    }
                }));
            }

            Task.WaitAll(tasks.ToArray());
            Debug.WriteLine($"Length after multithreaded adding and removing: {container.Length}");
        }

        [TestMethod]
        public void TestMultithreadedMixedOperations()
        {
            var container = new RequestContainer<MockRequest>();
            for (int i = 0; i < 1000; i++)
            {
                container.Add(new MockRequest());
            }

            var tasks = new List<Task>();
            for (int i = 0; i < 1000; i++)
            {
                tasks.Add(Task.Run(() =>
                {
                    var request = container[i % container.Length];
                    Assert.IsNotNull(request);
                }));

                tasks.Add(Task.Run(() =>
                {
                    container[i % container.Length] = new MockRequest();
                }));

                tasks.Add(Task.Run(() => container.Add(new MockRequest())));

                tasks.Add(Task.Run(() =>
                {
                    if (container.Length > 0)
                    {
                        container.Remove(container[0]);
                    }
                }));
            }

            Task.WaitAll(tasks.ToArray());
            Debug.WriteLine($"Length after multithreaded mixed operations: {container.Length}");
        }

        [TestMethod]
        public void TestMultithreadedFilling()
        {
            var container = new RequestContainer<MockRequest>();
            var tasks = new List<Task>();
            for (int i = 0; i < 1000; i++)
            {
                tasks.Add(Task.Run(() => container.Add(new MockRequest())));
            }
            Task.WaitAll(tasks.ToArray());
            Assert.AreEqual(1000, container.Length);
            Debug.WriteLine($"Length after multithreaded filling: {container.Length}");
        }

        [TestMethod]
        public async Task TestContainerTaskRemove()
        {
            // Arrange
            var container = new RequestContainer<OwnRequest>();
            var longRequest = new OwnRequest(async (token) => { await Task.Delay(10000, token); return true; });
            var request = new OwnRequest(async (token) => { await Task.Delay(5000, token); return true; });
            container.Add(new OwnRequest(async (token) => { await Task.Delay(3000, token); return true; }));

            // Act
            _ = Task.Run(async () => { await Task.Delay(2000); container.Add(longRequest); });
            _ = Task.Run(async () => { await Task.Delay(2000); container.Add(request); });
            _ = Task.Run(async () => { await Task.Delay(800); container.Remove(longRequest); });
            await container.Task;

            // Assert
            Console.WriteLine("Long Request State: " + longRequest.State);
            Console.WriteLine("Container State: " + container.State);
        }

        [TestMethod]
        public async Task TestContainerTask()
        {
            // Arrange
            var container = new RequestContainer<OwnRequest>();
            var longRequest = new OwnRequest(async (token) => { await Task.Delay(10000, token); return true; });
            var request = new OwnRequest(async (token) => { await Task.Delay(5000, token); return true; });
            container.Add(new OwnRequest(async (token) => { await Task.Delay(3000, token); return true; }));

            // Act
            _ = Task.Run(async () => { await Task.Delay(2000); container.Add(longRequest); });
            _ = Task.Run(async () => { await Task.Delay(2000); container.Add(request); });
            await container.Task;

            // Assert
            Console.WriteLine("Long Request State: " + longRequest.State);
            Console.WriteLine("Container State: " + container.State);
        }

        [TestMethod]
        public async Task TestContainerTaskFinished()
        {
            // Arrange
            var container = new RequestContainer<OwnRequest>();
            var longRequest = new OwnRequest(async (token) => { await Task.Delay(10000, token); return true; });
            container.Add(new OwnRequest(async (token) => { await Task.Delay(3000, token); return true; }));
            container.Add(new OwnRequest(async (token) => { await Task.Delay(1000, token); return true; }));

            // Act
            await container.Task;
            Console.WriteLine("Long Request State: " + longRequest.State);
            Console.WriteLine("Container State: " + container.State);
            container.Add(longRequest);
            Console.WriteLine("Container State: " + container.State);
            await container.Task;

            // Assert
            Console.WriteLine("End Container State: " + container.State);
            Console.WriteLine("Long Request State: " + longRequest.State);
        }
    }
}