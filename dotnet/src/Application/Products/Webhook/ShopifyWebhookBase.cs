using Infrastructure.Database.Entities;
using Integration.Aws.Sqs;

namespace Application.Products.Webhook;

public abstract class ShopifyWebhookBase
{
    protected static ShopifyProductVariantEntity ConstructEntity(SqsShopEventProduct product, SqsShopEventVariant variant)
    {
        return new ShopifyProductVariantEntity
        {
            GlobalProductId = product.AdminGraphqlApiId,
            ProductId = product.Id,
            GlobalVariantId = variant.AdminGraphqlApiId,
            VariantId = variant.Id,
            Sku = variant.Id.ToString(),
            Barcode = variant.Id.ToString()
        };
    }
}