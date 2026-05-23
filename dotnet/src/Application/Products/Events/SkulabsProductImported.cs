using SlimMessageBus;

namespace Application.Products.Events;

public readonly record struct SkulabsProductImportedEvent(Guid SkulabsProductId);

public class SkulabsProductImportedConsumer : IConsumer<SkulabsProductImportedEvent>
{
    public Task OnHandle(SkulabsProductImportedEvent message, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}