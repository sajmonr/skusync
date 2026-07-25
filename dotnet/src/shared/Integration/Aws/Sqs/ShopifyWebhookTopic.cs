namespace Integration.Aws.Sqs;

/// <summary>
/// The Shopify webhook topic identifiers the application subscribes to, as they appear in the
/// <c>X-Shopify-Topic</c> metadata header. Use these constants for <see cref="IShopifyWebhookHandler.TopicName"/>
/// rather than repeating the raw strings.
/// </summary>
public static class ShopifyWebhookTopic
{
    /// <summary>The <c>products/create</c> topic, emitted when a product is created in Shopify.</summary>
    public const string ProductsCreate = "products/create";

    /// <summary>The <c>products/update</c> topic, emitted when a product is updated in Shopify.</summary>
    public const string ProductsUpdate = "products/update";

    /// <summary>The <c>products/delete</c> topic, emitted when a product is deleted in Shopify.</summary>
    public const string ProductsDelete = "products/delete";
}
