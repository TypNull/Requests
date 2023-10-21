namespace Requests
{
    /// <summary>
    /// Interface to that contains a progress object
    /// </summary>
    public interface IProgressableRequest : IRequest
    {
        /// <summary>
        /// Progress object to return the progress value
        /// </summary>
        public Progress<float> Progress { get; }
    }
}
