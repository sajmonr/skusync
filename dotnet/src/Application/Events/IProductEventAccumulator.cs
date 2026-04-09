namespace Application.Events;

/// <summary>
/// Accumulates in-process <see cref="ProductChangedEvent"/> instances produced by
/// <c>ShopifyService</c> and the Shopify webhook handlers. A periodic Quartz job
/// drains the queue every N minutes and processes the collected batch.
/// </summary>
/// <remarks>
/// Implementations must be registered as a <b>singleton</b> so that all producers
/// share the same queue instance throughout the process lifetime.
/// </remarks>
public interface IProductEventAccumulator
{
    /// <summary>
    /// Adds a <see cref="ProductChangedEvent"/> to the accumulation queue.
    /// This method is safe to call from multiple threads concurrently.
    /// </summary>
    /// <param name="changedEvent">The event to enqueue.</param>
    void Enqueue(ProductChangedEvent changedEvent);

    /// <summary>
    /// Atomically removes and returns all events currently in the queue.
    /// After this call the internal queue is empty. Safe to call concurrently
    /// with <see cref="Enqueue"/>.
    /// </summary>
    /// <returns>
    /// A snapshot of every event that was in the queue at the time of the call.
    /// Returns an empty list when no events have been accumulated.
    /// </returns>
    IReadOnlyList<ProductChangedEvent> DrainAll();
}
