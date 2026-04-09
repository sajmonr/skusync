namespace Application.Events;

public static class EventAccumulatorExtensions
{

    extension<TEvent>(IEventAccumulator<TEvent> accumulator)
    {
        /// <summary>
        /// Enqueues a collection of events into the provided event accumulator.
        /// Each event in the collection is individually enqueued for processing.
        /// </summary>
        /// <typeparam name="TEvent">
        /// The type of events managed by the accumulator.
        /// </typeparam>
        /// <param name="events">
        /// The collection of events to be enqueued. Each event in the collection
        /// will be added to the accumulator.
        /// </param>
        public void Enqueue(IEnumerable<TEvent> events)
        {
            foreach (var @event in events)
            {
                accumulator.Enqueue(@event);
            }
        }
    }
    
}