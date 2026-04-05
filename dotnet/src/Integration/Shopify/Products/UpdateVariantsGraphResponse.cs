using Integration.Shopify.Responses;

namespace Integration.Shopify.Products;

/// <summary>
/// Represents the response received when updating product variants in Shopify using GraphQL.
/// </summary>
/// <remarks>
/// This record encapsulates potential user errors that may occur during the operation
/// of updating variants for a product in Shopify. If the operation is successful, the
/// <c>UserErrors</c> property will be <c>null</c>. Otherwise, it will contain a collection
/// of <c>UserErrorsResponse</c> providing detailed information about the errors encountered.
/// </remarks>
public record UpdateVariantsGraphResponse(UserErrorsResponse[]? UserErrors)
    ;