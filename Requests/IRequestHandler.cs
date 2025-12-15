namespace Requests
{
    /// <summary>
    /// Defines the contract for a request handler that manages the execution of <see cref="IRequest"/> instances.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Request handlers are responsible for coordinating the execution of multiple requests, providing
    /// shared resources such as cancellation tokens and synchronization contexts, and managing the overall
    /// lifecycle of request processing.
    public interface IRequestHandler : IRequestContainer<IRequest>
    {
        /// <summary>
        /// The common <see cref="System.Threading.CancellationToken"/> for all instances of the <see cref="IRequest"/> interface in this container.
        /// </summary>
        CancellationToken CancellationToken { get; }

        /// <summary>
        /// A default synchronization context that targets the ThreadPool.
        /// </summary>
        SynchronizationContext DefaultSynchronizationContext { get; }
    }
}
