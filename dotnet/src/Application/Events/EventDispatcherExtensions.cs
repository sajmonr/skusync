namespace Application.Events;

public static class EventDispatcherExtensions
{

    extension(IEventDispatcher eventDispatcher)
    {
        /// <summary>
        /// Dispatches a collection of events by invoking the corresponding handlers for each event.
        /// </summary>
        /// <typeparam name="TEvent">The type of the events to be dispatched.</typeparam>
        /// <param name="events">The collection of events to be dispatched.</param>
        public void DispatchMany<TEvent>(IEnumerable<TEvent> events)
        {
            foreach (var @event in events)
            {
                eventDispatcher.Dispatch(@event);
            }
        }
    }
    
}