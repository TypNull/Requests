using Requests.Options;

namespace Requests
{
    /// <summary>
    /// Represents a container that combines multiple <see cref="IRequest"/> instances.
    /// </summary>
    /// <typeparam name="TRequest">A class that implements <see cref="IRequest"/>.</typeparam>
    public interface IRequestContainer<TRequest> : IEnumerable<TRequest>, IRequest where TRequest : IRequest
    {
        /// <summary>
        /// Gets the count of <see cref="IRequest"/> instances contained in the <see cref="IRequestContainer{TRequest}"/>.
        /// </summary>
        int Count { get; }

        /// <summary>
        /// Waits for all currently pending requests in the container to complete.
        /// </summary>
        /// <param name="cancellationToken">A cancellation token to cancel the wait operation.</param>
        /// <returns>A task that completes when all current requests have finished.</returns>
        Task WaitForCurrentRequestsAsync(CancellationToken cancellationToken);

        /// <summary>
        /// Incorporates a <see cref="IRequest"/> into the <see cref="IRequestContainer{TRequest}"/>.
        /// </summary>
        /// <param name="request">The <see cref="IRequest"/> to be incorporated.</param>
        void Add(TRequest request);

        /// <summary>
        /// Incorporates multiple <see cref="IRequest"/> into the <see cref="IRequestContainer{TRequest}"/>.
        /// </summary>
        /// <param name="requests">The <see cref="IRequest"/> to be incorporated.</param>
        void AddRange(params TRequest[] requests);

        /// <summary>
        /// Removes a specific <see cref="IRequest"/> from this container.
        /// </summary>
        /// <param name="requests">The request to be removed.</param>
        void Remove(params TRequest[] requests);

        /// <summary>
        /// Sets the priority for the <see cref="IRequestContainer{TRequest}"/>.
        /// Not to the contained <see cref="IRequest"/> objects.
        /// </summary>
        void SetPriority(RequestPriority priority);
    }
}