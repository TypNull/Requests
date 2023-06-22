namespace Requests
{
    /// <summary>
    /// Combinies Requests and the Progressors of these
    /// </summary>
    /// <typeparam name="TRequest"></typeparam>
    public class ProgressableContainer<TRequest> : RequestContainer<TRequest> where TRequest : RequestObject, IProgressable
    {
        /// <summary>
        /// Mearged Progress of all Requests.
        /// </summary>
        public Progress<float> Progress => _progress;
        private readonly CombinableProgress _progress = new();

        /// <summary>
        /// Main Contructor for <see cref="ProgressableContainer{TRequest}"/>.
        /// </summary>
        public ProgressableContainer() { }

        /// <summary>
        /// Constructor to merge <see cref="RequestObject"/> together
        /// </summary>
        /// <param name="requests"><see cref="RequestObject"/>s to merge</param>
        public ProgressableContainer(params TRequest[] requests) => Add(requests);

        /// <summary>
        /// Creates a new <see cref="ProgressableContainer{TRequest}"/> that megres  <see cref="ProgressableContainer{TRequest}"/> together.
        /// </summary>
        /// <param name="requestContainers">Other <see cref="ProgressableContainer{TRequest}"/> to merge</param>
        /// <returns>A new <see cref="ProgressableContainer{TRequest}"/></returns>
        public static ProgressableContainer<TRequest> MergeContainers(/*bool autoReset = false,*/ params ProgressableContainer<TRequest>[] requestContainers)
        {
            List<TRequest> requests = new();
            Array.ForEach(requestContainers, requestContainer => requests.AddRange(requestContainer.GetRequests()));
            return new ProgressableContainer<TRequest>(requests.ToArray())
            {
                // AutoReset = autoReset
            };
        }

        /// <summary>
        /// Adds a <see cref="RequestObject"/> to the <see cref="ProgressableContainer{TRequest}"/>.
        /// </summary>
        /// <param name="request">The <see cref="RequestObject"/> to add.</param>
        public new void Add(TRequest request)
        {
            base.Add(request);
            AttachProgress(request);
        }

        private void AttachProgress(TRequest request)
        {
            if (request.Progress == null)
                return;
            //  if (!AutoReset)
            // {
            _progress.Attach(request.Progress);
            //    return;
            // }
            // request.StateChanged += AutoRemove;
        }

        //private void AutoRemove(TRequest? request)
        //{
        //    if (request?.StateChanged != null && request.StartOptions.Progress != null && request?.State is RequestState.Compleated or RequestState.Failed or RequestState.Cancelled)
        //    {
        //        _progress.TryRemove(request.StartOptions.Progress);
        //        request.StateChanged -= AutoRemove;
        //    }
        //}

        /// <summary>
        /// Adds a range <see cref="RequestObject"/> to the <see cref="ProgressableContainer{TRequest}"/>.
        /// </summary>
        /// <param name="requests">The <see cref="RequestObject"/> to add.</param>
        public override void Add(params TRequest[] requests)
        {
            base.Add(requests);
            Array.ForEach(requests, req => AttachProgress(req));
        }

        /// <summary>
        /// Removes a <see cref="RequestObject"/> from this container.
        /// </summary>
        /// <param name="requests">Request to remove</param>
        public override void Remove(params TRequest[] requests)
        {
            base.Remove(requests);
            Array.ForEach(requests, req =>
            {
                if (req.Progress != null)
                    _progress.TryRemove(req.Progress);
            });
        }


        /// <summary>
        /// Combines different Progressors into one.
        /// </summary>
        private class CombinableProgress : Progress<float>
        {
            private readonly List<Progress<float>> _progressors = new();
            private readonly List<float> _values = new();
            private readonly object _lock = new();

            /// <summary>
            /// Read-only property describing how many <see cref="Progress{T}"/> are in this <see cref="CombinableProgress"/>.
            /// </summary>
            public int Count => _progressors.Count;

            /// <summary>
            /// Initializes the <see cref="CombinableProgress"/>.
            /// </summary>
            public CombinableProgress() { }

            /// <summary>Initializes the <see cref="CombinableProgress"/> with the specified callback.</summary>
            /// <param name="handler">
            /// A handler to invoke for each reported progress value.  This handler will be invoked
            /// in addition to any delegates registered with the ProgressChanged event.
            /// Depending on the <see cref="SynchronizationContext"/> instance captured by
            /// the <see cref="Progress{T}"/> at construction, it's possible that this handler instance
            /// could be invoked concurrently with itself.
            /// </param>
            /// <exception cref="ArgumentNullException">The <paramref name="handler"/> is null.</exception>
            public CombinableProgress(Action<float> handler) : base(handler) { }

            /// <summary>
            /// Attach an element to the progress. If this does not work an overflow exception is thrown.
            /// </summary>
            /// <param name="progress"><see cref="Progress{T}"/> to attach</param>
            public void Attach(Progress<float> progress)
            {
                lock (_lock)
                {
                    _progressors.Add(progress);
                    _values.Add(0);
                }
                progress.ProgressChanged += OnProgressChanged;
            }

            /// <summary>
            /// Try's to remove a progress if it was attached in available 
            /// </summary>
            /// <param name="progress"><see cref="Progress{T}"/> to attach</param>
            /// <returns>bool which indicates success</returns>
            public bool TryRemove(Progress<float> progress)
            {
                lock (_lock)
                {
                    int index = _progressors.FindIndex(x => x == progress);
                    if (index == -1)
                        return false;
                    _progressors.RemoveAt(index);
                    _values.RemoveAt(index);
                }
                progress.ProgressChanged -= OnProgressChanged;
                return true;
            }

            /// <summary>
            /// Will be called if one Progress changed
            /// </summary>
            /// <param name="sender"></param>
            /// <param name="e"></param>
            private void OnProgressChanged(object? sender, float e)
            {
                double average = 0f;
                lock (_lock)
                    average = Calculate(sender, e);
                OnReport((float)average);
            }

            private double Calculate(object? progress, float value)
            {
                double average = 0;
                int n = _progressors.Count;
                for (int i = 0; i < n; i++)
                {
                    if (ReferenceEquals(_progressors[i], progress))
                        _values[i] = value;
                    average += _values[i];
                }
                return average /= n;
            }
        }
    }
}
