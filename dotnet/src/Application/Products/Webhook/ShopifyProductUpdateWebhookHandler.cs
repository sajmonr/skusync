using Application.Products.Events;
using Application.Products.Services;
using Infrastructure.Database;
using Infrastructure.Database.Entities;
using Integration.Aws.Sqs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SlimMessageBus;

namespace Application.Products.Webhook;

/// <summary>
/// Handles the <c>products/update</c> Shopify webhook topic. Reconciles incoming variant
/// data with the local database — creating new variant records for any variants not yet
/// tracked and updating titles for existing ones — then pushes any SKU or barcode
/// discrepancies back to Shopify.
/// </summary>
public class ShopifyProductUpdateWebhookHandler(
    ApplicationDbContext dbContext,
    ILogger<ShopifyProductUpdateWebhookHandler> logger,
    IMessageBus messageBus)
    : ShopifyWebhookBase, IShopifyWebhookHandler
{
    /// <inheritdoc/>
    public string TopicName => "products/update";

    /// <summary>
    /// Reconciles the incoming product payload with local database state, then synchronises
    /// any changed variants back to Shopify.
    /// </summary>
    /// <param name="product">The product payload from the <c>products/update</c> webhook.</param>
    public async Task Handle(SqsShopEventProduct product)
    {
        var existingVariants = await dbContext.ShopifyProductVariants.Where(variant => variant.ProductId == product.Id)
            .ToArrayAsync();

        logger.LogDebug(
            "Loaded {Count} variants for product {ProductId}. We currently have {ExistingCount} variants.",
            product.Variants.Count, product.Id, existingVariants.Length);

        // Collect events before SaveChangesAsync so we only publish for persisted changes.
        var createdEntities = new List<ShopifyProductVariantEntity>();
        var updatedEntities = new List<ShopifyProductVariantEntity>();

        // update entities
        foreach (var variant in product.Variants)
        {
            var entity = existingVariants.FirstOrDefault(e => e.VariantId == variant.Id);

            if (entity is null)
            {
                var newEntity = ConstructEntity(product, variant);

                newEntity.LogEvents.Add(new ShopifyProductVariantLogEventEntity
                {
                    Message = VariantLogMessages.VariantCreated()
                });

                dbContext.ShopifyProductVariants.Add(newEntity);
                createdEntities.Add(newEntity);
            }
            else
            {
                // Update it
                var didChange = UpdateEntity(entity, product, variant);
                var didBarcodeOrSkuChange = DidBarcodeOrSkuChange(entity, variant);

                if (!didChange && !didBarcodeOrSkuChange)
                {
                    continue;
                }

                updatedEntities.Add(entity);
            }
        }

        await dbContext.SaveChangesAsync();

        // Enqueue only after a successful save so no phantom events enter the queue.
        var updatedEvents = updatedEntities.Select(e => new ProductVariantUpdatedEvent(e.ShopifyProductVariantId));
        var createdEvents = createdEntities.Select(e => new ProductVariantCreatedEvent(e.ShopifyProductVariantId));
        
        await messageBus.Publish(updatedEvents);
        await messageBus.Publish(createdEvents);
    }

    private bool UpdateEntity(ShopifyProductVariantEntity entity, SqsShopEventProduct product,
        SqsShopEventVariant variant)
    {
        var changed = false;
        
        if (entity.DisplayName != variant.DisplayName)
        {
            dbContext.ShopifyProductVariantLogEvents.Add(new ShopifyProductVariantLogEventEntity
            {
                ShopifyProductVariantId = entity.ShopifyProductVariantId,
                Message = VariantLogMessages.TitleUpdated(entity.DisplayName, variant.DisplayName)
            });
            logger.LogDebug("Updating display name for variant {VariantId}: [{OldName}] -> [{NewName}].", variant.Id, entity.DisplayName, variant.DisplayName);
            entity.DisplayName = variant.DisplayName;
        }
        
        return changed;
    }

    private bool DidBarcodeOrSkuChange(ShopifyProductVariantEntity entity, SqsShopEventVariant variant)
    {
        if (!string.IsNullOrEmpty(entity.Barcode) && entity.Barcode != variant.Barcode)
        {
            logger.LogDebug("Barcode for variant {VariantId} does not match in Shopify. Updating it.",
                variant.Id);

            return true;
        }

        if (!string.IsNullOrEmpty(entity.Sku) && entity.Sku != variant.Sku)
        {
            logger.LogDebug("SKU for variant {VariantId} does not match in Shopify. Updating it.",
                variant.Id);

            return true;
        }

        return false;
    }
}