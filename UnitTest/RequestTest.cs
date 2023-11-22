using Requests;

namespace UnitTest
{
    [TestClass]
    public class RequestTest
    {
        [TestMethod]
        public async Task ContainerTaskRemoveTest()
        {
            RequestContainer<OwnRequest> container = new();
            OwnRequest longRequest = new(async (token) => { await Task.Delay(10000); return true; });
            OwnRequest request = new(async (token) => { await Task.Delay(5000); return true; });
            container.Add(new OwnRequest(async (token) => { await Task.Delay(3000); return true; }));
            _ = Task.Run(async () => { await Task.Delay(2000); container.Add(longRequest); });
            _ = Task.Run(async () => { await Task.Delay(2000); container.Add(request); });
            _ = Task.Run(async () => { await Task.Delay(800); container.Remove(longRequest); });
            await container.Task;
            Console.WriteLine("Long Request State: " + longRequest.State);
            Console.WriteLine("Container State: " + container.State);
        }

        [TestMethod]
        public async Task ContainerTaskTest()
        {
            RequestContainer<OwnRequest> container = new();
            OwnRequest longRequest = new(async (token) => { await Task.Delay(10000); return true; });
            OwnRequest request = new(async (token) => { await Task.Delay(5000); return true; });
            container.Add(new OwnRequest(async (token) => { await Task.Delay(3000); return true; }));
            _ = Task.Run(async () => { await Task.Delay(2000); container.Add(longRequest); });
            _ = Task.Run(async () => { await Task.Delay(2000); container.Add(request); });
            await container.Task;
            Console.WriteLine("Long Request State: " + longRequest.State);
            Console.WriteLine("Container State: " + container.State);
        }

        [TestMethod]
        public async Task ContainerTaskFinishedTest()
        {
            RequestContainer<OwnRequest> container = new();
            OwnRequest longRequest = new(async (token) => { await Task.Delay(10000); return true; });
            container.Add(new(async (token) => { await Task.Delay(3000); return true; }));
            container.Add(new OwnRequest(async (token) => { await Task.Delay(1000); return true; }));
            await container.Task;
            Console.WriteLine("Long Request State: " + longRequest.State);
            Console.WriteLine("Container State: " + container.State);
            container.Add(longRequest);
            Console.WriteLine("Container State: " + container.State);
            await container.Task;
            Console.WriteLine("End Container State: " + container.State);
            Console.WriteLine("Long Request State: " + longRequest.State);
        }
    }
}