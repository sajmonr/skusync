using Application.Events;
using Application.Products.Events;
using Application.Products.Services;
using Infrastructure.Database;
using Infrastructure.Database.Entities;
using Integration.Aws.Sqs;
using Microsoft.Extensions.Logging;

namespace Application.Products.Webhook;

/// <summary>
/// Handles the <c>products/create</c> Shopify webhook topic. When a new product is created
/// in Shopify, this handler persists each of its variants to the local database and then
/// writes the generated SKU and barcode back to Shopify.
/// </summary>
public class ShopifyProductCreateWebhookHandler(
    ApplicationDbContext dbContext,
    ILogger<ShopifyProductUpdateWebhookHandler> logger,
    IEventAccumulator<ProductChangedEvent> eventAccumulator)
    : ShopifyWebhookBase, IShopifyWebhookHandler
{

    /// <inheritdoc/>
    public string TopicName => "products/create";

    /// <summary>
    /// Persists all variants of the newly created product and pushes the generated SKU
    /// and barcode values back to Shopify via the Admin API.
    /// </summary>
    /// <param name="product">The product payload from the <c>products/create</c> webhook.</param>
    public async Task Handle(SqsShopEventProduct product)
    {
        var entities = new List<ShopifyProductVariantEntity>();
        
        foreach (var variant in product.Variants)
        {
            logger.LogInformation(
                "New variant {VariantId} [{VariantTitle} for product {ProductId} [{ProductTitle}] found.",
                variant.Id, variant.Title, product.Id, product.Title);

            var newEntity = ConstructEntity(product, variant);

            newEntity.LogEvents.Add(new ShopifyProductVariantLogEventEntity
            {
                Message = VariantLogMessages.VariantCreated()
            });

            entities.Add(newEntity);
        }

        await dbContext.ShopifyProductVariants.AddRangeAsync(entities);
        await dbContext.SaveChangesAsync();

        // Enqueue only after a successful save so no phantom events enter the queue.
        foreach (var entity in entities)
        {
            eventAccumulator.Enqueue(new ProductChangedEvent(entity.VariantId, ProductChangeType.Created));
        }
    }
}