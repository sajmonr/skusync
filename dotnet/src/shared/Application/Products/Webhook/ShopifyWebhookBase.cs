using Infrastructure.Database.Entities;
using Integration.Aws.Sqs;
using Integration.Shopify.Products;

namespace Application.Products.Webhook;

public abstract class ShopifyWebhookBase
{
    /// <summary>
    /// Mirrors one variant from a webhook payload — a straight copy of what Shopify said, with no
    /// decisions taken.
    /// <para>
    /// Notably it records Shopify's SKU and barcode verbatim, including empty ones. Substituting a
    /// generated value here would make the row claim Shopify holds something it does not, and the
    /// reconciler compares against exactly this row to work out what Shopify is owed. Deciding what
    /// the codes <em>should</em> be is the merge rules' job, and their answer lands in the desired
    /// state instead.
    /// </para>
    /// </summary>
    protected static ShopifyProductVariantEntity ConstructEntity(
        SqsShopEventProduct product,
        SqsShopEventVariant variant
    )
    {
        return new ShopifyProductVariantEntity
        {
            GlobalProductId = product.AdminGraphqlApiId,
            ProductId = product.Id,
            GlobalVariantId = variant.AdminGraphqlApiId,
            VariantId = variant.Id,
            DisplayName = ShopifyDisplayName.Compose(product.Title, variant.Title),
            ProductTitle = product.Title ?? "",
            VariantTitle = variant.Title ?? "",
            Sku = variant.Sku ?? "",
            Barcode = variant.Barcode ?? "",
        };
    }
}
