using Infrastructure.Database.Entities;
using Integration.Aws.Sqs;

namespace Application.Products.Webhook;

public abstract class ShopifyWebhookBase
{
    /// <summary>
    /// Sentinel variant title Shopify uses for products with no option variants.
    /// When the variant title is this value, the variant's display name is just the
    /// product title (no trailing " - Default Title" suffix).
    /// </summary>
    private const string DefaultVariantTitle = "Default Title";

    protected static ShopifyProductVariantEntity ConstructEntity(SqsShopEventProduct product, SqsShopEventVariant variant)
    {
        return new ShopifyProductVariantEntity
        {
            GlobalProductId = product.AdminGraphqlApiId,
            ProductId = product.Id,
            GlobalVariantId = variant.AdminGraphqlApiId,
            VariantId = variant.Id,
            DisplayName = ResolveDisplayName(product, variant),
            Sku = variant.Id.ToString(),
            Barcode = variant.Id.ToString()
        };
    }

    /// <summary>
    /// Builds the display name for a variant from the webhook payload. Shopify webhooks
    /// don't carry a <c>display_name</c> field — only product.title and variant.title —
    /// so we compose it ourselves. For variants without options (variant title is
    /// "Default Title"), the display name is just the product title; otherwise it is
    /// <c>"{product.title} - {variant.title}"</c>.
    /// </summary>
    protected static string ResolveDisplayName(SqsShopEventProduct product, SqsShopEventVariant variant)
    {
        return variant.Title == DefaultVariantTitle
            ? product.Title
            : $"{product.Title} - {variant.Title}";
    }
}