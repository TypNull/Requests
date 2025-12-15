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
        public static ProgressableContainer<TRequest> MergeContainers(params ProgressableContainer<TRequest>[] requestContainers)
        {
            ProgressableContainer<TRequest> container = [];
            Array.ForEach(requestContainers, requestContainer => container.AddRange([.. requestContainer]));
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
            if (request.Progress != null)
                _progress.Attach(request.Progress);
        }

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
        /// Combines different progress trackers into one using incremental average calculation.
        /// </summary>
        private sealed class CombinableProgress : Progress<float>
        {
            private readonly List<Progress<float>> _progressors = [];
            private readonly List<float> _values = [];
            private float _currentAverage = 0f;
            private readonly object _lock = new();

            /// <summary>
            /// Gets the count of attached <see cref="Progress{T}"/> instances.
            /// </summary>
            public int Count
            {
                get
                {
                    lock (_lock)
                        return _progressors.Count;
                }
            }

            /// <summary>
            /// Attaches a progress tracker to this CombinableProgress instance.
            /// </summary>
            /// <param name="progress">The <see cref="Progress{T}"/></param>
            public void Attach(Progress<float> progress)
            {
                ArgumentNullException.ThrowIfNull(progress);

                lock (_lock)
                {
                    _progressors.Add(progress);
                    _values.Add(0f);
                    RecalculateAverage();
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
                ArgumentNullException.ThrowIfNull(progress);

                lock (_lock)
                {
                    int index = _progressors.IndexOf(progress);
                    if (index == -1)
                        return false;

                    _progressors.RemoveAt(index);
                    _values.RemoveAt(index);

                    if (_progressors.Count > 0)
                        RecalculateAverage();
                    else
                        _currentAverage = 0f;
                }

                progress.ProgressChanged -= OnProgressChanged;
                return true;
            }

            /// <summary>
            /// Called when any attached progress tracker reports a change in progress.
            /// Uses incremental average calculation for O(1) performance.
            /// </summary>
            /// <param name="sender">The sender object.</param>
            /// <param name="newValue">The new progress value.</param>
            private void OnProgressChanged(object? sender, float newValue)
            {
                float average;

                lock (_lock)
                {
                    int index = _progressors.FindIndex(p => ReferenceEquals(p, sender));
                    if (index == -1)
                        return;

                    float oldValue = _values[index];
                    _values[index] = newValue;

                    // Incremental average update - O(1) operation
                    // Formula: new_avg = old_avg + (new_val - old_val) / count
                    int count = _progressors.Count;
                    if (count > 0)
                        _currentAverage += (newValue - oldValue) / count;

                    average = _currentAverage;
                }

                OnReport(average);
            }

            /// <summary>
            /// Recalculates the average from scratch. Used when count changes.
            /// </summary>
            private void RecalculateAverage()
            {
                int count = _progressors.Count;
                if (count == 0)
                {
                    _currentAverage = 0f;
                    return;
                }

                float sum = 0f;
                foreach (float value in _values)
                    sum += value;

                _currentAverage = sum / count;
            }
        }
    }
}
