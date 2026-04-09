namespace Application.Events;

/// <summary>
/// Represents an interface for accumulating and managing a collection of events.
/// Provides functionality to enqueue events and retrieve them in bulk for processing.
/// </summary>
/// <typeparam name="TEvent">
/// The type of events managed by the accumulator.
/// </typeparam>
public interface IEventAccumulator<TEvent>
{
    /// <summary>
    /// Adds an event to the queue for future processing.
    /// </summary>
    /// <param name="changedEvent">
    /// The event to be enqueued. This represents a single unit of work or a change that needs to be processed.
    /// </param>
    void Enqueue(TEvent changedEvent);

    /// <summary>
    /// Drains all accumulated events from the accumulator and returns them as a read-only list.
    /// </summary>
    /// <returns>
    /// A read-only list of accumulated events. If no events are present, the list will be empty.
    /// </returns>
    IReadOnlyList<TEvent> DrainAll();
}
