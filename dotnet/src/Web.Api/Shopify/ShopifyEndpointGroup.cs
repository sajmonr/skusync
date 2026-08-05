using FastEndpoints;
using Web.Api.Shopify.Authentication;

namespace Web.Api.Shopify;

/// <summary>
/// The endpoint group every Shopify-facing endpoint belongs to. It owns the shared route prefix,
/// enforces the Shopify session-token scheme, and opens up CORS for the extension sandbox origin —
/// so an endpoint added under <c>Shopify/Features</c> cannot accidentally ship unauthenticated.
/// </summary>
public sealed class ShopifyEndpointGroup : Group
{
    public ShopifyEndpointGroup()
    {
        Configure("shopify", endpoint =>
        {
            endpoint.AuthSchemes(ShopifyAuthenticationDefaults.AuthenticationScheme);
            endpoint.Policies(ShopifyAuthenticationDefaults.PolicyName);
            endpoint.Options(builder => builder.RequireCors(ShopifyExtensionCors.PolicyName));
            endpoint.Description(builder => builder.WithTags("Shopify"));
        });
    }
}
