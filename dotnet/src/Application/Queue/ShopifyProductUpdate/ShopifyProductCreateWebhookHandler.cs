using Application.Events;
using Infrastructure.Database;
using Infrastructure.Database.Entities;
using Integration.Aws.Sqs;
using Integration.Shopify.Products;
using Microsoft.Extensions.Logging;

namespace Application.Queue.ShopifyProductUpdate;

/// <summary>
/// Handles the <c>products/create</c> Shopify webhook topic. When a new product is created
/// in Shopify, this handler persists each of its variants to the local database and then
/// writes the generated SKU and barcode back to Shopify.
/// </summary>
public class ShopifyProductCreateWebhookHandler(
    ApplicationDbContext dbContext,
    IShopifyProductService productService,
    ILogger<ShopifyProductUpdateWebhookHandler> logger,
    IProductEventAccumulator eventAccumulator)
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
            
            entities.Add(newEntity);
        }

        await dbContext.ShopifyProductVariants.AddRangeAsync(entities);
        await dbContext.SaveChangesAsync();

        // Enqueue only after a successful save so no phantom events enter the queue.
        foreach (var entity in entities)
        {
            eventAccumulator.Enqueue(new ProductChangedEvent(entity.VariantId, ProductChangeType.Created));
        }

        await CreateBarcodeAndSkuInShopify(product.AdminGraphqlApiId, entities);
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