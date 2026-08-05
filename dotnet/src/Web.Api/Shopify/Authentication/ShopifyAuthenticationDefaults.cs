namespace Web.Api.Shopify.Authentication;

/// <summary>
/// Names shared by the Shopify session-token authentication scheme, its authorization policy, and
/// the claims the handler puts on the resulting principal.
/// </summary>
public static class ShopifyAuthenticationDefaults
{
    /// <summary>The authentication scheme that validates Shopify session tokens.</summary>
    public const string AuthenticationScheme = "ShopifySessionToken";

    /// <summary>The authorization policy every Shopify endpoint requires.</summary>
    public const string PolicyName = "shopify-session-token";

    /// <summary>
    /// The claim holding the shop the token was issued for, taken from the token's <c>dest</c>
    /// claim. Requiring it stops a dashboard cookie from satisfying a Shopify endpoint.
    /// </summary>
    public const string ShopClaimType = "shopify:shop";

    /// <summary>The session-token claim naming the shop the request originated from.</summary>
    public const string DestinationClaimType = "dest";
}
