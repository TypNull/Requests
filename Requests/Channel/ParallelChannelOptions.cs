namespace Requests.Channel
{
    /// <summary>
    /// Represents a configuration class for setting the maximum degree of parallelism in a channel.
    /// </summary>
    public class ParallelChannelOptions : ParallelOptions
    {
        /// <summary>
        /// The maximum degree of parallelism for the channel.
        /// </summary>
        private int _maxDegreeOfParallelism = Environment.ProcessorCount;

        /// <summary>
        /// Occurs when the maximum degree of parallelism changes.
        /// </summary>
        public event EventHandler<int>? DegreeOfParallelismChangedDelta;

        /// <summary>
        /// Gets or sets the pause token that can be used to easily end the process.
        /// </summary>
        public PauseToken EasyEndToken { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ParallelChannelOptions"/> class.
        /// </summary>
        public ParallelChannelOptions() => base.MaxDegreeOfParallelism = int.MaxValue;

        /// <summary>
        /// Gets or sets the maximum degree of parallelism for the channel.
        /// </summary>
        /// <value>
        /// The maximum degree of parallelism.
        /// </value>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when the value is less than zero.
        /// </exception>
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
