namespace Application.Events;

public static class ProductEventAccumulatorExtensions
{

    extension(IProductEventAccumulator accumulator)
    {
        /// <summary>
        /// Adds a collection of <see cref="ProductChangedEvent"/> instances to the accumulator for processing.
        /// </summary>
        /// <param name="events">
        /// A collection of <see cref="ProductChangedEvent"/> instances representing product variant changes
        /// to be added to the accumulator.
        /// </param>
        public void Enqueue(IEnumerable<ProductChangedEvent> events)
        {
            foreach (var @event in events)
            {
                accumulator.Enqueue(@event);
            }
        }
    }
    
}