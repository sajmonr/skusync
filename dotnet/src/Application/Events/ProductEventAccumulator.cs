using System.Collections.Concurrent;

namespace Application.Events;

/// <summary>
/// Thread-safe, in-memory implementation of <see cref="IProductEventAccumulator"/> backed
/// by a <see cref="ConcurrentQueue{T}"/>. Register this class as a singleton so that all
/// producers (webhook handlers, import service) share the same queue.
/// </summary>
public sealed class ProductEventAccumulator : IProductEventAccumulator
{
    private readonly ConcurrentQueue<ProductChangedEvent> _queue = new();

    /// <inheritdoc/>
    public void Enqueue(ProductChangedEvent changedEvent) => _queue.Enqueue(changedEvent);

    /// <inheritdoc/>
    public IReadOnlyList<ProductChangedEvent> DrainAll()
    {
        var drained = new List<ProductChangedEvent>();

        while (_queue.TryDequeue(out var item))
        {
            drained.Add(item);
        }

        return drained;
    }
}
