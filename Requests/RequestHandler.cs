using Requests.Channel;
using Requests.Options;

namespace Requests
{
    /// <summary>
    /// The <see cref="RequestHandler"/> class is responsible for executing instances of the <see cref="IRequest"/> interface.
    /// </summary>
    public class RequestHandler
    {
        /// <summary>
        /// A priority channel that queues all incoming instances of the <see cref="IRequest"/> interface.
        /// </summary>
        private readonly PriorityChannel<IRequest> _requestsChannel = new(3);

        /// <summary>
        /// Property indicating whether the <see cref="RequestHandler"/> is currently running.
        /// </summary>
        public bool IsRunning { get; private set; }

        /// <summary>
        /// Property that sets the degree of parallel execution of instances of the <see cref="IRequest"/> interface. 
        /// Disables AutoParallelism when set. Default value is null.
        /// </summary>
        public int? StaticDegreeOfParallelism
        {
            get => _staticDegreeOfParallelism; set
            {
                _requestsChannel.Options.MaxDegreeOfParallelism = value ?? AutoParallelism.Invoke();
                _staticDegreeOfParallelism = value;
            }
        }
        private int? _staticDegreeOfParallelism;

        /// <summary>
        /// A function that calculates the degree of parallel execution of instances of the <see cref="IRequest"/> interface dynamically while running.
        /// </summary>
        public Func<int> AutoParallelism { get; set; } = () => Environment.ProcessorCount;

        /// <summary>
        /// Property that sets the maximum possible degree of parallel execution of instances of the <see cref="IRequest"/> interface.
        /// </summary>
        public int MaxParallelism { get => _maxParallelism; set { if (value < 0) throw new ArgumentOutOfRangeException(nameof(MaxParallelism)); _maxParallelism = value; } }
        private int _maxParallelism = Environment.ProcessorCount * 2;

        private CancellationTokenSource _cts = new();
        private readonly PauseTokenSource _pts = new();

        /// <summary>
        /// The main <see cref="System.Threading.CancellationToken"/> for all instances of the <see cref="IRequest"/> interface.
        /// </summary>
        public CancellationToken CancellationToken => _cts.Token;

        /// <summary>
        /// Two primary handlers to handle instances of the <see cref="IRequest"/> interface.
        /// </summary>
        public static RequestHandler[] MainRequestHandlers { get; } = new RequestHandler[] { new(), new() };

        /// <summary>
        /// A default synchronization context that targets the ThreadPool.
        /// </summary>
        public readonly SynchronizationContext DefaultSynchronizationContext = new();

        /// <summary>
        /// The number of instances of the <see cref="IRequest"/> interface that are not yet handled.
        /// </summary>
        public int CountRequests => _requestsChannel.Reader.Count;

        /// <summary>
        /// Constructor for the <see cref="RequestHandler"/> class.
        /// </summary>
        /// <param name="requests">Instances of the <see cref="IRequest"/> interface that should be added.</param>
        public RequestHandler(params IRequest[] requests)
        {
            AddRequest(requests);
            _requestsChannel.Options.EasyEndToken = _pts.Token;
            _requestsChannel.Options.MaxDegreeOfParallelism = Math.Min(AutoParallelism.Invoke(), MaxParallelism);
        }

        /// <summary>
        /// Method to add a single instance of the <see cref="IRequest"/> interface to the handler.
        /// </summary>
        /// <param name="request">The instance of the <see cref="IRequest"/> interface that should be added.</param>
        public void AddRequest(IRequest request)
        => _ = _requestsChannel.Writer.WriteAsync(new((int)request.Priority, request)).AsTask();


        /// <summary>
        /// Method to add multiple instances of the <see cref="IRequest"/> interface to the handler.
        /// </summary>
        /// <param name="requests">The instances of the <see cref="IRequest"/> interface that should be added.</param>
        public void AddRequest(params IRequest[] requests)
        => Array.ForEach(requests, request => _ = _requestsChannel.Writer.WriteAsync(new((int)request.Priority, request)).AsTask());


        /// <summary>
        /// Method to run the instance of the <see cref="IRequest"/> interface and add instances of the <see cref="IRequest"/> interface.
        /// </summary>
        /// <param name="request">The instance of the <see cref="IRequest"/> interface that should be added.</param>
        public void RunRequests(IRequest request)
        {
            AddRequest(request);
            RunRequests();
        }

        /// <summary>
        /// Executes the provided instances of the <see cref="IRequest"/> interface and adds them to the request queue.
        /// </summary>
        /// <param name="requests">Instances of the <see cref="IRequest"/> interface that should be added.</param>
        public void RunRequests(params IRequest[] requests)
        {
            AddRequest(requests);
            RunRequests();
        }

        /// <summary>
        /// Resumes the execution of instances of the <see cref="IRequest"/> interface if the handler was previously paused.
        /// </summary>
        public void Resume()
        {
            if (!_requestsChannel.Options.EasyEndToken.IsPaused)
                return;
            _pts.Resume();
            if (CountRequests > 0)
                RunRequests();
        }

        /// <summary>
        /// Pauses the execution of instances of the <see cref="IRequest"/> interface, allowing any currently running requests to complete.
        /// </summary>
        public void Pause() => _pts.Pause();

        /// <summary>
        /// Creates a new <see cref="CancellationTokenSource"/> if the previous one was canceled.
        /// </summary>
        public void CreateCTS()
        {
            if (_cts.IsCancellationRequested)
            {
                _cts.Dispose();
                _cts = new CancellationTokenSource();
                _requestsChannel.Options.CancellationToken = CancellationToken;
                if (CountRequests > 0)
                    RunRequests();
            }
        }

        /// <summary>
        /// Creates a new <see cref="CancellationTokenSource"/> for all main RequestHandlers.
        /// </summary>
        public static void CreateMainCTS() => Array.ForEach(MainRequestHandlers, handler => handler.CreateCTS());

        /// <summary>
        /// Cancels the main <see cref="CancellationTokenSource"/> for all instances of the <see cref="IRequest"/> interface in this RequestHandler.
        /// </summary>
        public void CancelCTS() => _cts.Cancel();

        /// <summary>
        /// Cancels the main <see cref="CancellationTokenSource"/> for all instances of the <see cref="IRequest"/> interface in the Main RequestHandlers.
        /// </summary>
        public static void CancelMainCTS() => Array.ForEach(MainRequestHandlers, handler => handler.CancelCTS());

        /// <summary>
        /// Pauses the execution of instances of the <see cref="IRequest"/> interface for all Main RequestHandlers, allowing any currently running requests to complete.
        /// </summary>
        public static void PauseMain() => Array.ForEach(MainRequestHandlers, handler => handler.Pause());

        /// <summary>
        /// Resumes the execution of instances of the <see cref="IRequest"/> interface for all Main RequestHandlers if they were previously paused.
        /// </summary>
        public static void ReusmeMain() => Array.ForEach(MainRequestHandlers, handler => handler.Resume());


        /// <summary>
        /// This method is responsible for executing the instances of the  <see cref="IRequest"/> if the handler is not currently running.
        /// It updates the degree of parallelism based on the current system environment and runs the request channel.
        /// </summary>
        public void RunRequests()
        {
            if (IsRunning || CancellationToken.IsCancellationRequested || _pts.IsPaused)
                return;
            IsRunning = true;
            UpdateAutoParallelism();
            Task.Run(async () => await RunChannel());
        }

        /// <summary>
        /// This method is responsible for running the request channel in parallel.
        /// </summary>
        /// <returns>async Task to await</returns>
        private async Task RunChannel()
        {
            await _requestsChannel.RunParallelReader(async (pair, ct) => await HandleRequests(pair));
            IsRunning = false;
            if (_requestsChannel.Reader.Count != 0)
                RunRequests();
        }

        /// <summary>
        /// This method is responsible for handling a given request. It starts the request and based on the state of the request.
        /// </summary>
        /// <param name="pair">Priority request pair</param>
        /// <returns>async Task to await</returns>
        private async Task HandleRequests(PriorityItem<IRequest> pair)
        {
            IRequest request = pair.Item;
            await request.StartRequestAsync();

            if (request.State is RequestState.Compleated or RequestState.Failed or RequestState.Cancelled)
                request.Dispose();
            else if (request.State == RequestState.Idle)
                await _requestsChannel.Writer.WriteAsync(pair);
        }

        /// <summary>
        /// Updates the degree of parallelism for executing instances of the <see cref="IRequest"/> interface based on the current system environment.
        /// </summary>
        public void UpdateAutoParallelism()
        {
            if (StaticDegreeOfParallelism == null)
                _requestsChannel.Options.MaxDegreeOfParallelism = Math.Min(AutoParallelism.Invoke(), MaxParallelism);
        }
    }
}
