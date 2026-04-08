using Infrastructure.Database;
using Infrastructure.Database.Entities;
using Integration.Aws.Sqs;
using Integration.Shopify.Products;
using Microsoft.Extensions.Logging;

namespace Application.Queue.ShopifyProductUpdate;

public class ShopifyProductCreateWebhookHandler(
    ApplicationDbContext dbContext,
    IShopifyProductService productService,
    ILogger<ShopifyProductUpdateWebhookHandler> logger)
    : IShopifyWebhookHandler
{
    public string TopicName => "products/create";

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