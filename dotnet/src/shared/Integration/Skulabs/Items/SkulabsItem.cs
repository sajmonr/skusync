namespace Integration.Skulabs.Items;

/// <summary>
/// Represents a SkuLabs inventory item mapped from the SkuLabs API response,
/// enriched with the linked Shopify variant identifier.
/// </summary>
public record SkuLabsItem(
    string SkulabsItemId,
    string SkulabsListingId,
    long ShopifyVariantId,
    string Sku,
    string Barcode,
    string Title);
