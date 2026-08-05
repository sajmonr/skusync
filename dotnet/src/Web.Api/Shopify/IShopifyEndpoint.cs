namespace Web.Api.Shopify;

/// <summary>
/// Marks an endpoint as belonging to the Shopify surface. The global endpoint configurator in
/// <c>Program.cs</c> secures everything else with the dashboard's cookie policy, which a Shopify
/// session token can never satisfy; implementing this hands authorization to
/// <see cref="ShopifyEndpointGroup"/> instead.
/// </summary>
public interface IShopifyEndpoint;
