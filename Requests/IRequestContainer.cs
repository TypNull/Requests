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
        /// Represents a task that completes when all the requests currently available in the container have completed.
        /// This task does not include requests that may be added to the container in the future.
        /// </summary>
        Task CurrentTask { get; }

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