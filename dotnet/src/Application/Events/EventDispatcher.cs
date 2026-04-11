using Microsoft.Extensions.DependencyInjection;

namespace Application.Events;

internal class EventDispatcher(IServiceProvider serviceProvider) : IEventDispatcher
{
    public void Dispatch<TEvent>(TEvent @event)
    {
        var accumulator = serviceProvider.GetRequiredService<IEventAccumulator<TEvent>>();
        accumulator.Enqueue(@event);
    }
}