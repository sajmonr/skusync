namespace Web.Api.Shopify.Features.GetProductInformation;

/// <param name="ProductId">The numeric Shopify product ID the lookup resolved to.</param>
/// <param name="Variants">
/// The product's variants known to SkuSync, ordered by SKU. Variants Shopify has but SkuSync has
/// never ingested are absent rather than listed without a link.
/// </param>
public readonly record struct GetProductInformationResponse(
    long ProductId,
    IReadOnlyList<ProductVariantInformation> Variants);

/// <param name="VariantId">The numeric Shopify product variant ID.</param>
/// <param name="Sku">The variant's SKU, empty when it has none yet.</param>
/// <param name="Title">The variant's display name, as <c>Product (Variant)</c>.</param>
/// <param name="SkulabsUrl">The SkuLabs admin URL, or <c>null</c> when the variant is unlinked.</param>
public readonly record struct ProductVariantInformation(
    long VariantId,
    string Sku,
    string Title,
    string? SkulabsUrl);
