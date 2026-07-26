namespace Integration.Skulabs.Items;

/// <summary>
/// A SkuLabs item that maps to more than one Shopify listing and so must be surfaced for review
/// instead of synced. Carries the item metadata plus every Shopify listing so the ambiguity can be
/// examined and resolved in SkuLabs.
/// </summary>
public readonly record struct SkulabsAmbiguousItem(
    string SourceItemId,
    string Name,
    string Sku,
    string Upc,
    IReadOnlyList<SkulabsApiListing> Listings);
