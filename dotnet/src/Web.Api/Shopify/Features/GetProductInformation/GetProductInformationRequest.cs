namespace Web.Api.Shopify.Features.GetProductInformation;

public class GetProductInformationRequest
{
    /// <summary>
    /// Gets or sets the Shopify product to look up, accepted either as the Admin GraphQL global ID an
    /// extension reads off its render target (<c>gid://shopify/Product/987654321</c>) or as the bare
    /// numeric ID.
    /// </summary>
    public string? ProductId { get; set; }
}
