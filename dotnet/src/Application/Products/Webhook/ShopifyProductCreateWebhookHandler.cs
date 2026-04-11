using Application.Events;
using Application.Products.Events;
using Infrastructure.Database;
using Infrastructure.Database.Entities;
using Integration.Aws.Sqs;
using Integration.Shopify.Products;
using Microsoft.Extensions.Logging;
using Microsoft.FeatureManagement;

namespace Application.Products.Webhook;

/// <summary>
/// Handles the <c>products/create</c> Shopify webhook topic. When a new product is created
/// in Shopify, this handler persists each of its variants to the local database and then
/// writes the generated SKU and barcode back to Shopify.
/// </summary>
public class ShopifyProductCreateWebhookHandler(
    ApplicationDbContext dbContext,
    IShopifyProductService productService,
    ILogger<ShopifyProductUpdateWebhookHandler> logger,
    IEventAccumulator<ProductChangedEvent> eventAccumulator,
    IFeatureManager featureManager)
    : IShopifyWebhookHandler
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

            var newEntity = new ShopifyProductVariantEntity
            {
                GlobalProductId = product.AdminGraphqlApiId,
                ProductId = product.Id,
                GlobalVariantId = variant.AdminGraphqlApiId,
                VariantId = variant.Id,
                ProductTitle = product.Title,
                VariantTitle = variant.Title,
                Sku = variant.Id.ToString(),
                Barcode = variant.Id.ToString()
            };

            newEntity.LogEvents.Add(new ShopifyProductVariantLogEventEntity
            {
                Message = "Product variant was created."
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

        if (await featureManager.IsEnabledAsync(FeatureFlags.ShopifyWriteBack))
        {
            await CreateBarcodeAndSkuInShopify(product.AdminGraphqlApiId, entities);
        }
        else
        {
            logger.LogInformation(
                "ShopifyWriteBack feature flag is disabled. Skipping Shopify write-back for product {ProductId}.",
                product.AdminGraphqlApiId);
        }
    }

    private async Task CreateBarcodeAndSkuInShopify(string productId, IEnumerable<ShopifyProductVariantEntity> entities)
    {
        var entitiesToUpdate = entities
            .Select(e => new ShopifyUpdateProductVariant(e.GlobalVariantId, e.Sku, e.Barcode)).ToArray();

        logger.LogDebug("Updating {Count} variants in Shopify from {TopicName} webhook.", entitiesToUpdate.Length,
            TopicName);

        await productService.UpdateVariants(productId,
            entitiesToUpdate);
    }

}