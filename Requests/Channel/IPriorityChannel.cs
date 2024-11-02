using System.Threading.Channels;

namespace Requests.Channel
{
    /// <summary>
    /// Represents a priority channel that allows reading and writing of items with associated priorities.
    /// </summary>
    /// <typeparam name="TElement">The type of elements in the channel.</typeparam>
    public interface IPriorityChannel<TElement>
    {
        /// <summary>
        /// Gets the reader for the priority channel, which allows reading items with their associated priorities.
        /// </summary>
        ChannelReader<PriorityItem<TElement>> Reader { get; }

        /// <summary>
        /// Gets the writer for the priority channel, which allows writing items with their associated priorities.
        /// </summary>
        ChannelWriter<PriorityItem<TElement>> Writer { get; }

        /// <summary>
        /// Gets the options for configuring the behavior of the parallel channel, such as maximum degree of parallelism.
        /// </summary>
        ParallelChannelOptions Options { get; }

        /// <summary>
        /// Gets the current count of items in the data structure.
        /// </summary>
        public int Count { get; }

        /// <summary>
        /// Executes a parallel reader operation on the priority channel, processing items with their associated priorities.
        /// </summary>
        /// <param name="body">The function to execute for each item in the channel.</param>
        /// <returns>A task that represents the completion of the parallel reader operation.</returns>
        Task RunParallelReader(Func<PriorityItem<TElement>, CancellationToken, ValueTask> body);

        /// <summary>
        /// Converts the items in the priority channel to an array, preserving their order and priorities.
        /// </summary>
        /// <returns>An array of items with their associated priorities.</returns>
        PriorityItem<TElement>[] ToArray();

        /// <summary>
        /// Attempts to remove the specified item from the priority channel.
        /// </summary>
        /// <param name="item">The item to remove.</param>
        /// <returns>True if the item was successfully removed; otherwise, false.</returns>
        bool TryRemove(PriorityItem<TElement> item);
    }
}