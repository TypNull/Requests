namespace Requests
{
    /// <summary>
    /// Interface to that contains a progress object
    /// </summary>
    public interface IProgressable
    {
        /// <summary>
        /// Progress to return the progress value
        /// </summary>
        public Progress<float> Progress { get; }
    }
}
