namespace Requests
{
    /// <summary>
    /// Represents an interface that includes a progress object.
    /// </summary>
    public interface IProgressableRequest : IRequest
    {
        /// <summary>
        /// Gets the progress object to retrieve the progress value.
        /// </summary>
        public Progress<float> Progress { get; }
    }
}