using SharedKernel;

namespace Integration.Skulabs.Items;

/// <summary>
/// A SkuLabs item that failed the strict "exactly one Shopify listing" criterion and so must be
/// surfaced for review instead of synced. Carries the item metadata plus every listing (Shopify or
/// not) so the ambiguity can be examined and remapped later.
/// </summary>
public readonly record struct SkulabsAmbiguousItem(
    string SourceItemId,
    string Name,
    string Sku,
    string Upc,
    SkulabsAmbiguityReason Reason,
    IReadOnlyList<SkulabsApiListing> Listings);
