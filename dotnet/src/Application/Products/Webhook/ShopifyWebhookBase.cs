using Infrastructure.Database.Entities;
using Integration.Aws.Sqs;
using Integration.Shopify.Products;

namespace Application.Products.Webhook;

public abstract class ShopifyWebhookBase(IShopifyProductService productService)
{
    protected static ShopifyProductVariantEntity ConstructEntity(SqsShopEventProduct product, SqsShopEventVariant variant)
    {
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

    protected async Task SetBarcodeAndSkuInShopify(string productId, IEnumerable<ShopifyProductVariantEntity> entities)
    {
        var entitiesToUpdate = entities
            .Select(e => new ShopifyUpdateProductVariant(e.GlobalVariantId, e.Sku, e.Barcode)).ToArray();

        await productService.UpdateVariants(productId,
            entitiesToUpdate);
    }
}