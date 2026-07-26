namespace Integration.Skulabs.Items;

/// <summary>
/// A SkuLabs inventory item with <em>all</em> of its channel listings preserved. This is the raw
/// shape returned by the client — no listings are dropped or flattened. Callers decide which items
/// are syncable versus ambiguous via <see cref="SkulabsItemCollection"/>.
/// </summary>
public readonly record struct SkulabsApiItem(
    string SourceItemId,
    string Name,
    string Sku,
    string Upc,
    IReadOnlyList<SkulabsApiListing> Listings);
