namespace Requests
{
    /// <summary>
    /// Combines requests and their progress indicators.
    /// </summary>
    /// <typeparam name="TRequest">Type of requests to merge.</typeparam>
    public class ProgressableContainer<TRequest> : RequestContainer<TRequest>, IProgressableRequest where TRequest : IProgressableRequest
    {
        /// <summary>
        /// Merged progress of all requests.
        /// </summary>
        public Progress<float> Progress => _progress;
        private readonly CombinableProgress _progress = new();

        /// <summary>
        /// Main constructor for <see cref="ProgressableContainer{TRequest}"/>.
        /// </summary>
        public ProgressableContainer() { }

        /// <summary>
        /// Constructor to merge multiple <see cref="IRequest"/> instances.
        /// </summary>
        /// <param name="requests">Requests to merge.</param>
        public ProgressableContainer(params TRequest[] requests) => AddRange(requests);

        /// <summary>
        /// Creates a new <see cref="ProgressableContainer{TRequest}"/> by merging other <see cref="ProgressableContainer{TRequest}"/> instances.
        /// </summary>
        /// <param name="requestContainers">Other containers to merge.</param>
        /// <returns>A new merged container.</returns>
        public static ProgressableContainer<TRequest> MergeContainers(/*bool autoReset = false,*/ params ProgressableContainer<TRequest>[] requestContainers)
        {
            ProgressableContainer<TRequest> container = new();
            Array.ForEach(requestContainers, requestContainer => container.AddRange(requestContainer.ToArray()));
            return container;
        }

        /// <summary>
        /// Adds an <see cref="IProgressableRequest"/> to the <see cref="ProgressableContainer{TRequest}"/>.
        /// </summary>
        /// <param name="request">The request to add.</param>
        public new void Add(TRequest request)
        {
            base.Add(request);
            AttachProgress(request);
        }

        /// <summary>
        /// Accesses all <see cref="IProgressableRequest"/> to the <see cref="ProgressableContainer{TRequest}"/>.
        /// </summary>
        /// <returns>Returns an array of <see cref="IProgressableRequest"/> instances.</returns>
        public override TRequest this[int key]
        {
            get => base[key];
            set
            {
                _progress.TryRemove(base[key].Progress);
                base[key] = value;
                _progress.Attach(base[key].Progress);
            }
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
        /// Adds a range of <see cref="IProgressableRequest"/> instances to the container.
        /// </summary>
        /// <param name="requests">Requests to add.</param>
        public override void AddRange(params TRequest[] requests)
        {
            base.AddRange(requests);
            Array.ForEach(requests, req => AttachProgress(req));
        }

        /// <summary>
        /// Removes one or more <see cref="IProgressableRequest"/> instances from this container.
        /// </summary>
        /// <param name="requests">Requests to remove.</param>
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
        /// Combines different progress trackers into one.
        /// </summary>
        private class CombinableProgress : Progress<float>
        {
            private readonly List<Progress<float>> _progressors = new();
            private readonly List<float> _values = new();
            private readonly object _lock = new();

            /// <summary>
            /// Gets the count of attached <see cref="Progress{T}"/> instances.
            /// </summary>
            public int Count => _progressors.Count;

            /// <summary>
            /// Initializes a new instance of the <see cref="CombinableProgress"/> class.
            /// </summary>
            public CombinableProgress() { }

            /// <summary>
            /// Initializes a new instance of the <see cref="CombinableProgress"/> class with the specified callback.
            /// </summary>
            /// <param name="handler">
            /// A handler to invoke for each reported progress value. This handler will be invoked
            /// in addition to any delegates registered with the ProgressChanged event.
            /// Depending on the SynchronizationContext instance captured by
            /// the Progress{T} at construction, it's possible that this handler instance
            /// could be invoked concurrently with itself.
            /// </param>
            /// <exception cref="ArgumentNullException">The handler is null.</exception>
            public CombinableProgress(Action<float> handler) : base(handler) { }

            /// <summary>
            /// Attaches a progress tracker to this CombinableProgress instance.
            /// </summary>
            /// <param name="progress">The <see cref="Progress{T}"/></param>
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
            /// Attempts to remove an attached progress tracker.
            /// </summary>
            /// <param name="progress">The <see cref="Progress{T}"/> instance to remove.</param>
            /// <returns>True if removal was successful; otherwise, false.</returns>
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
            /// Called when any attached progress tracker reports a change in progress.
            /// </summary>
            /// <param name="sender">The sender object.</param>
            /// <param name="e">The progress value.</param>
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
