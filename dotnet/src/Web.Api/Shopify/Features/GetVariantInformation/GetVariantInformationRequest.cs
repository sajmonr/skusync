namespace Web.Api.Shopify.Features.GetVariantInformation;

public class GetVariantInformationRequest
{
    /// <summary>
    /// Gets or sets the Shopify product variant to look up, accepted either as the Admin GraphQL
    /// global ID an extension reads off its render target
    /// (<c>gid://shopify/ProductVariant/987654321</c>) or as the bare numeric ID.
    /// </summary>
    public string? VariantId { get; set; }
}
