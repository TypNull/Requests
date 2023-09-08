using Requests.Channel;
using Requests.Options;

namespace Requests
{
    /// <summary>
    /// Class that executes the Requests
    /// </summary>
    public class RequestHandler
    {
        /// <summary>
        /// Priority Channel that holds all Requests
        /// </summary>
        private readonly PriorityChannel<IRequest> _requestsChannel = new(3);

        /// <summary>
        /// Indicates if the <see cref="RequestHandler"/> is running.
        /// </summary>
        public bool IsRunning { get; private set; }

        /// <summary>
        /// Sets the degree of parallel requests that will be handled. 
        /// Disables AutoParallelism.
        /// Default value is null
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
        /// A function to calculate the parallelism while running
        /// </summary>
        public Func<int> AutoParallelism { get; set; } = () => Environment.ProcessorCount;

        /// <summary>
        /// Value that is the higest possible degree of parallelism
        /// </summary>
        public int MaxParallelism { get => _maxParallelism; set { if (value < 0) throw new ArgumentOutOfRangeException(nameof(MaxParallelism)); _maxParallelism = value; } }
        private int _maxParallelism = Environment.ProcessorCount * 2;

        private CancellationTokenSource _cts = new();
        private readonly PauseTokenSource _pts = new();
        /// <summary>
        /// Main <see cref="CancellationToken"/> for all <see cref="IRequest.IRequestObject"/>s.
        /// </summary>
        public CancellationToken CT => _cts.Token;

        /// <summary>
        /// Two main handlers to handel requests.
        /// </summary>
        public static RequestHandler[] MainRequestHandlers { get; } = new RequestHandler[] { new(), new() };

        /// <summary>
        /// Requests that are not yet Handeled
        /// </summary>
        public int CountRequests => _requestsChannel.Reader.Count;

        /// <summary>
        /// Constructor for <see cref="RequestHandler"/> class.
        /// </summary>
        /// <param name="requests">Requests that sould be added</param>
        public RequestHandler(params IRequest[] requests)
        {
            AddRequest(requests);
            _requestsChannel.Options.EasyEndToken = _pts.Token;
            _requestsChannel.Options.MaxDegreeOfParallelism = Math.Min(AutoParallelism.Invoke(), MaxParallelism);
        }

        /// <summary>
        /// Adds Requests to the handler
        /// </summary>
        /// <param name="request">Requests that sould be added</param>
        public void AddRequest(IRequest request)
        => _ = _requestsChannel.Writer.WriteAsync(new((int)request.Priority, request)).AsTask();


        /// <summary>
        /// Adds Requests to the handler
        /// </summary>
        /// <param name="requests">Requests that sould be added</param>
        public void AddRequest(params IRequest[] requests)
        => Array.ForEach(requests, request => _ = _requestsChannel.Writer.WriteAsync(new((int)request.Priority, request)).AsTask());


        /// <summary>
        /// Runs the Request and adds Requests
        /// </summary>
        /// <param name="request">Requests that sould be added</param>
        public void RunRequests(IRequest request)
        {
            AddRequest(request);
            RunRequests();
        }

        /// <summary>
        /// Runs the Request and adds Requests
        /// </summary>
        /// <param name="requests">Requests that sould be added</param>
        public void RunRequests(params IRequest[] requests)
        {
            AddRequest(requests);
            RunRequests();
        }

        /// <summary>
        /// Resumes the handler if it was paused
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
        /// Pause the handler.
        /// It lets running requests complete
        /// </summary>
        public void Pause() => _pts.Pause();

        /// <summary>
        /// Creates a new <see cref="CancellationTokenSource"/> if the old one was canceled.
        /// </summary>
        public void CreateCTS()
        {
            if (_cts.IsCancellationRequested)
            {
                _cts.Dispose();
                _cts = new CancellationTokenSource();
                _requestsChannel.Options.CancellationToken = CT;
                if (CountRequests > 0)
                    RunRequests();
            }
        }

        /// <summary>
        /// Creates a new <see cref="CancellationTokenSource"/> for all main RequestHandlers
        /// </summary>
        public static void CreateMainCTS() => Array.ForEach(MainRequestHandlers, handler => handler.CreateCTS());

        /// <summary>
        /// Cancels the main <see cref="CancellationTokenSource"/> for all Requests in this RequestHandler.
        /// </summary>
        public void CancelCTS() => _cts.Cancel();

        /// <summary>
        /// Cancels the main <see cref="CancellationTokenSource"/> for all Requests in the Main RequestHandlers.
        /// </summary>
        public static void CancelMainCTS() => Array.ForEach(MainRequestHandlers, handler => handler.CancelCTS());

        /// <summary>
        ///  Pause the handler for all Requests in the Main RequestHandlers.
        /// It lets running requests complete
        /// </summary>
        public static void PauseMain() => Array.ForEach(MainRequestHandlers, handler => handler.Pause());

        /// <summary>
        /// Resumes all Requests in the Main RequestHandlers if it was paused
        /// It lets running requests complete
        /// </summary>
        public static void ReusmeMain() => Array.ForEach(MainRequestHandlers, handler => handler.Resume());


        /// <summary>
        /// Runs the Request if it is not running
        /// </summary>
        public void RunRequests()
        {
            if (IsRunning || CT.IsCancellationRequested || _pts.IsPaused)
                return;
            IsRunning = true;
            UpdateAutoParallelism();
            Task.Run(async () => await RunChannel());
        }

        private async Task RunChannel()
        {
            await _requestsChannel.RunParallelReader(async (pair, ct) => await HandleRequests(pair));
            IsRunning = false;
            if (_requestsChannel.Reader.Count != 0)
                RunRequests();
        }

        private async Task HandleRequests(PriorityItem<IRequest> pair)
        {
            IRequest request = pair.Item;
            await request.StartRequestAsync();

            if (request.State is RequestState.Compleated or RequestState.Failed or RequestState.Cancelled)
                request.Dispose();
            else if (request.State == RequestState.Available)
                await _requestsChannel.Writer.WriteAsync(pair);
        }

        /// <summary>
        /// Call to calculate and update the AutoParallelism
        /// </summary>
        public void UpdateAutoParallelism()
        {
            if (StaticDegreeOfParallelism == null)
                _requestsChannel.Options.MaxDegreeOfParallelism = Math.Min(AutoParallelism.Invoke(), MaxParallelism);
        }
    }
}
