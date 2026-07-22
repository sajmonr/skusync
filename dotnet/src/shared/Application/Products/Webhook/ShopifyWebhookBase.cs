using Infrastructure.Database.Entities;
using Integration.Aws.Sqs;
using Integration.Shopify.Products;

namespace Application.Products.Webhook;

public abstract class ShopifyWebhookBase
{
    protected static ShopifyProductVariantEntity ConstructEntity(
        SqsShopEventProduct product,
        SqsShopEventVariant variant,
        string sku
    )
    {
        return new ShopifyProductVariantEntity
        {
            GlobalProductId = product.AdminGraphqlApiId,
            ProductId = product.Id,
            GlobalVariantId = variant.AdminGraphqlApiId,
            VariantId = variant.Id,
            DisplayName = ShopifyDisplayName.Compose(product.Title, variant.Title),
            Sku = sku,
            Barcode = variant.Id.ToString(),
        };
    }
}
