using System.Collections.Concurrent;

namespace Application.Events;

/// <summary>
/// Represents an implementation of <see cref="IEventAccumulator{TEvent}"/> for managing
/// a thread-safe collection of events. This class allows for events to be enqueued and
/// retrieved in bulk while ensuring thread safety throughout its operations.
/// </summary>
/// <typeparam name="TEvent">
/// The type of events managed by the accumulator.
/// </typeparam>
public sealed class EventAccumulator<TEvent> : IEventAccumulator<TEvent>
{
    private readonly ConcurrentQueue<TEvent> _queue = new();

    /// <inheritdoc/>
    public void Enqueue(TEvent changedEvent) => _queue.Enqueue(changedEvent);

    /// <inheritdoc/>
    public IReadOnlyList<TEvent> DrainAll()
    {
        var drained = new List<TEvent>();

        while (_queue.TryDequeue(out var item))
        {
            drained.Add(item);
        }

        return drained;
    }
}
