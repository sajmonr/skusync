namespace Application.Events;

/// <summary>
/// Defines a contract for dispatching events to their respective handlers.
/// </summary>
public interface IEventDispatcher
{
    /// <summary>
    /// Dispatches the specified event to its corresponding handlers.
    /// </summary>
    /// <typeparam name="TEvent">The type of the event to be dispatched.</typeparam>
    /// <param name="event">The event instance to be dispatched.</param>
    void Dispatch<TEvent>(TEvent @event);
    
}