using SlimMessageBus;

namespace Application.Products.Events;

public readonly record struct ProductVariantCreatedEvent(Guid ProductVariantId);

public class ProductVariantCreatedConsumer : IConsumer<ProductVariantCreatedEvent>
{
    public async Task OnHandle(ProductVariantCreatedEvent message, CancellationToken cancellationToken)
    {
        // implement
    }
}