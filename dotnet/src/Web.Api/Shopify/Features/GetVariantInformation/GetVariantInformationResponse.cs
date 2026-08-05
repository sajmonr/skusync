namespace Web.Api.Shopify.Features.GetVariantInformation;

/// <param name="VariantId">The numeric Shopify product variant ID the lookup resolved to.</param>
/// <param name="SkulabsItemId">The linked SkuLabs item's source ID.</param>
/// <param name="SkulabsUrl">The SkuLabs admin URL for the linked item.</param>
public readonly record struct GetVariantInformationResponse(
    long VariantId,
    string SkulabsItemId,
    string SkulabsUrl);
