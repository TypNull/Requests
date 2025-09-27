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

            public bool HasCompleted()
            {
                throw new NotImplementedException();
            }
        }

        [TestMethod]
        public void TestAddAndLength()
        {
            RequestContainer<MockRequest> container = new()
            { new() };
            Assert.AreEqual(1, container.Count);
            Debug.WriteLine($"Count after adding one request: {container.Count}");
        }

        [TestMethod]
        public void TestAddRangeAndLength()
        {
            RequestContainer<MockRequest> container = new();
            container.AddRange(new MockRequest(), new MockRequest());
            Assert.AreEqual(2, container.Count);
            Debug.WriteLine($"Count after adding two requests: {container.Count}");
        }

        [TestMethod]
        public void TestGetEnumerator()
        {
            RequestContainer<MockRequest> container = new();
            container.AddRange(new MockRequest(), new MockRequest());
            IEnumerator<MockRequest> enumerator = container.GetEnumerator();
            int count = 0;
            while (enumerator.MoveNext())
                count++;
            Assert.AreEqual(2, count);
            Debug.WriteLine($"Enumerated {count} requests");
        }

        [TestMethod]
        public void TestSetIdleWhileFilling()
        {
            RequestContainer<MockRequest> container = new();
            MockRequest request1 = new();
            MockRequest request2 = new();
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
            RequestContainer<MockRequest> container = new();
            MockRequest request1 = new();
            MockRequest request2 = new();
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
            RequestContainer<MockRequest> container = new();
            MockRequest request1 = new();
            MockRequest request2 = new();
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
            RequestContainer<MockRequest> container = new();
            MockRequest request1 = new();
            MockRequest request2 = new();
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
            RequestContainer<MockRequest> container1 = new();
            RequestContainer<MockRequest> container2 = new();
            container1.Add(new MockRequest());
            container2.Add(new MockRequest());
            RequestContainer<MockRequest> mergedContainer = RequestContainer<MockRequest>.MergeContainers(container1, container2);
            Assert.AreEqual(2, mergedContainer.Count);
            Debug.WriteLine($"Count of merged container: {mergedContainer.Count}");
        }

        [TestMethod]
        public void TestAccessItems()
        {
            RequestContainer<MockRequest> container = new();
            MockRequest request1 = new();
            MockRequest request2 = new();
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
            RequestContainer<MockRequest> container = new();
            for (int i = 0; i < 1000; i++)
            {
                container.Add(new MockRequest());
            }

            List<Task> tasks = new();
            for (int i = 0; i < 1000; i++)
            {
                tasks.Add(Task.Run(() =>
                {
                    MockRequest request = container[i % container.Count];
                    Assert.IsNotNull(request);
                }));

                tasks.Add(Task.Run(() =>
                {
                    container[i % container.Count] = new MockRequest();
                }));
            }

            Task.WaitAll(tasks.ToArray());
            Assert.AreEqual(1000, container.Count);
            Debug.WriteLine($"Count after multithreaded reading and writing: {container.Count}");
        }

        [TestMethod]
        public void TestMultithreadedAddingAndRemoving()
        {
            RequestContainer<MockRequest> container = new();
            List<Task> tasks = new();

            for (int i = 0; i < 1000; i++)
            {
                tasks.Add(Task.Run(() => container.Add(new MockRequest())));
                tasks.Add(Task.Run(() =>
                {
                    if (container.Count > 0)
                    {
                        container.Remove(container[0]);
                    }
                }));
            }

            Task.WaitAll(tasks.ToArray());
            Debug.WriteLine($"Count after multithreaded adding and removing: {container.Count}");
        }

        [TestMethod]
        public void TestMultithreadedMixedOperations()
        {
            RequestContainer<MockRequest> container = new();
            for (int i = 0; i < 1000; i++)
            {
                container.Add(new MockRequest());
            }

            List<Task> tasks = new();
            for (int i = 0; i < 1000; i++)
            {
                tasks.Add(Task.Run(() =>
                {
                    MockRequest request = container[i % container.Count];
                    Assert.IsNotNull(request);
                }));

                tasks.Add(Task.Run(() =>
                {
                    container[i % container.Count] = new MockRequest();
                }));

                tasks.Add(Task.Run(() => container.Add(new MockRequest())));

                tasks.Add(Task.Run(() =>
                {
                    if (container.Count > 0)
                    {
                        container.Remove(container[0]);
                    }
                }));
            }

            Task.WaitAll(tasks.ToArray());
            Debug.WriteLine($"Count after multithreaded mixed operations: {container.Count}");
        }

        [TestMethod]
        public void TestMultithreadedFilling()
        {
            RequestContainer<MockRequest> container = new();
            List<Task> tasks = new();
            for (int i = 0; i < 1000; i++)
            {
                tasks.Add(Task.Run(() => container.Add(new MockRequest())));
            }
            Task.WaitAll(tasks.ToArray());
            Assert.AreEqual(1000, container.Count);
            Debug.WriteLine($"Count after multithreaded filling: {container.Count}");
        }

        [TestMethod]
        public async Task TestContainerTaskRemove()
        {
            // Arrange
            RequestContainer<OwnRequest> container = new();
            OwnRequest longRequest = new(async (token) => { await Task.Delay(3500, token); return true; });
            OwnRequest request = new(async (token) => { await Task.Delay(5000, token); return true; });
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
            RequestContainer<OwnRequest> container = new();
            OwnRequest longRequest = new(async (token) => { await Task.Delay(1500, token); return true; });
            OwnRequest request = new(async (token) => { await Task.Delay(5000, token); return true; });
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
            RequestContainer<OwnRequest> container = new();
            OwnRequest longRequest = new(async (token) => { await Task.Delay(2000, token); return true; });
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