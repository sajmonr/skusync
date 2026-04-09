using Infrastructure.Database;
using Infrastructure.Database.Entities;
using Integration.Aws.Sqs;
using Integration.Shopify.Products;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Queue.ShopifyProductUpdate;

/// <summary>
/// Handles the <c>products/update</c> Shopify webhook topic. Reconciles incoming variant
/// data with the local database — creating new variant records for any variants not yet
/// tracked and updating titles for existing ones — then pushes any SKU or barcode
/// discrepancies back to Shopify.
/// </summary>
public class ShopifyProductUpdateWebhookHandler(
    ApplicationDbContext dbContext,
    IShopifyProductService productService,
    ILogger<ShopifyProductUpdateWebhookHandler> logger)
    : IShopifyWebhookHandler
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
        var toUpdateInShopify = new List<ShopifyProductVariantEntity>();

        logger.LogDebug(
            "Loaded {Count} variants for product {ProductId} [{ProductTitle}]. We currently have {ExistingCount} variants.",
            product.Variants.Count, product.Id, product.Title, existingVariants.Length);

        // update entities
        foreach (var variant in product.Variants)
        {
            var entity = existingVariants.FirstOrDefault(e => e.VariantId == variant.Id);

            if (entity is null)
            {
                var newEntity = CreateEntity(product, variant);

                dbContext.ShopifyProductVariants.Add(newEntity);
                toUpdateInShopify.Add(newEntity);
            }
            else
            {
                // Update it
                UpdateEntity(entity, product, variant);

                if (RequiresUpdateInShopify(entity, product, variant))
                {
                    toUpdateInShopify.Add(entity);
                }
            }
        }

        await dbContext.SaveChangesAsync();

        await UpdateEntitiesInShopify(product.AdminGraphqlApiId, toUpdateInShopify);
    }

    private async Task UpdateEntitiesInShopify(string productId, IEnumerable<ShopifyProductVariantEntity> entities)
    {
        var entitiesToUpdate = entities
            .Select(e => new ShopifyUpdateProductVariant(e.GlobalVariantId, e.Sku, e.Barcode)).ToArray();

        logger.LogDebug("Updating {Count} variants in Shopify from {TopicName} webhook.", entitiesToUpdate.Length,
            TopicName);

        await productService.UpdateVariants(productId,
            entitiesToUpdate);
    }

    private void UpdateEntity(ShopifyProductVariantEntity entity, SqsShopEventProduct product,
        SqsShopEventVariant variant)
    {
        if (entity.ProductTitle != product.Title)
        {
            logger.LogDebug("Updating product title for variant {VariantId}: [{OldTitle}] -> [{NewTitle}].",
                variant.Id, entity.ProductTitle, product.Title);
            entity.ProductTitle = product.Title;
            entity.UpdatedOnUtc = DateTime.UtcNow;
        }

        if (variant.Title != "Default Title" && entity.VariantTitle != variant.Title)
        {
            logger.LogDebug("Updating variant title for variant {VariantId}: [{OldTitle}] -> [{NewTitle}].",
                variant.Id, entity.VariantTitle, variant.Title);
            entity.VariantTitle = variant.Title;
            entity.UpdatedOnUtc = DateTime.UtcNow;
        }
    }

    private bool RequiresUpdateInShopify(ShopifyProductVariantEntity entity, SqsShopEventProduct product,
        SqsShopEventVariant variant)
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

    private ShopifyProductVariantEntity CreateEntity(SqsShopEventProduct product, SqsShopEventVariant variant)
    {
        logger.LogInformation(
            "New variant {VariantId} [{VariantTitle} for product {ProductId} [{ProductTitle}] found.",
            variant.Id, variant.Title, product.Id, product.Title);

        // Create it
        return new ShopifyProductVariantEntity
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
    }
}