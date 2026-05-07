using SlimMessageBus;

namespace Application.Products.Events;

public readonly record struct SkulabsProductImportedEvent(Guid SkulabsProductId);

public class SkulabsProductImportedConsumer : IConsumer<SkulabsProductImportedEvent>
{
    public async Task OnHandle(SkulabsProductImportedEvent message, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }
}