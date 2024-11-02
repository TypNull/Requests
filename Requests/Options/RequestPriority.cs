namespace Requests.Options
{
    /// <summary>
    /// Struct to define the priority levels of a request using floating-point values.
    /// </summary>
    public readonly struct RequestPriority
    {
        private readonly float value = Normal;

        /// <summary>
        /// Represents the highest priority level for a request.
        /// </summary>
        public static readonly RequestPriority High = 0;

        /// <summary>
        /// Represents the default or normal priority level for a request.
        /// </summary>
        public static readonly RequestPriority Normal = 1;

        /// <summary>
        /// Represents the lowest priority level for a request.
        /// </summary>
        public static readonly RequestPriority Low = 2;

        private RequestPriority(float value) => this.value = value;


        /// <summary>
        /// Implicit conversion from RequestPriority to float.
        /// </summary>
        public static implicit operator float(RequestPriority priority) => priority.value;

        /// <summary>
        /// Implicit conversion from float to RequestPriority.
        /// </summary>
        public static implicit operator RequestPriority(float value) => new(value);

        /// <summary>
        /// Override ToString to return the value as a string.
        /// </summary>
        public override string ToString() => value.ToString();
    }
}