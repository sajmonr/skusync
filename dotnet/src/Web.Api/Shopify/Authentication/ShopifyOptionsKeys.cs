using Integration.Shopify.GraphQl;

namespace Web.Api.Shopify.Authentication;

/// <summary>
/// Configuration paths this layer reads out of the shared <see cref="ShopifyOptions"/> section. The
/// session-token scheme is built before the DI container exists, so it reads the shop URL from
/// configuration directly rather than through <c>IOptions</c>.
/// </summary>
public static class ShopifyOptionsKeys
{
    public const string ShopUrl = $"{ShopifyOptions.SectionKey}:ShopUrl";
}
