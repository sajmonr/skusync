using SlimMessageBus;

namespace Application.Products.Events;

public readonly record struct ProductVariantUpdatedEvent(Guid ProductVariantId);

public class ProductVariantUpdatedConsumer : IConsumer<ProductVariantUpdatedEvent>
{
    public async Task OnHandle(ProductVariantUpdatedEvent message, CancellationToken cancellationToken)
    {
        // implement
    }
}