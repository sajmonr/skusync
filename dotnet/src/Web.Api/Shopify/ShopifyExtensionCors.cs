namespace Web.Api.Shopify;

/// <summary>
/// The CORS policy applied to Shopify endpoints.
/// </summary>
public static class ShopifyExtensionCors
{
    public const string PolicyName = "shopify-extension";

    /// <summary>
    /// The origin admin UI extensions run under. Extension code is sandboxed in an iframe served
    /// from Shopify's CDN in every environment — including under <c>shopify app dev</c>, where only
    /// the bundle is served locally — so this single origin covers local and deployed alike.
    /// </summary>
    public const string ExtensionOrigin = "https://extensions.shopifycdn.com";
}
