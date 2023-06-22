namespace Requests.Channel
{
    /// <summary>
    /// Stores options that configure the degree of max parallism in for the channel
    /// </summary>
    public class ParallelChannelOptions : ParallelOptions
    {
        private int _maxDegreeOfParallelism = Environment.ProcessorCount;

        /// <summary>
        /// Event that notifys the changed degree of parallelism
        /// </summary>
        public event EventHandler<int>? DegreeOfParallelismChangedDelta;

        /// <summary>
        /// Pause token
        /// </summary>
        public PauseToken EasyEndToken { get; set; }

        /// <summary>
        /// Main contructor
        /// </summary>
        public ParallelChannelOptions() => base.MaxDegreeOfParallelism = int.MaxValue;

        /// <summary>
        /// Maximal degree of parallelism of the channel
        /// </summary>
        public new int MaxDegreeOfParallelism
        {
            get => _maxDegreeOfParallelism;
            set
            {
                if (value < 0) throw new ArgumentOutOfRangeException(nameof(MaxDegreeOfParallelism));
                if (value == _maxDegreeOfParallelism) return;
                int delta = value - _maxDegreeOfParallelism;
                DegreeOfParallelismChangedDelta?.Invoke(this, delta);
                _maxDegreeOfParallelism = value;
            }
        }
    }
}
