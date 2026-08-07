namespace Integration.Skulabs.Items;

/// <summary>
/// A SkuLabs inventory item with <em>all</em> of its channel listings preserved. This is the raw
/// shape returned by the client — no listings are dropped or flattened. Callers decide which items
/// are syncable versus ambiguous via <see cref="SkulabsItemCollection"/>.
/// </summary>
/// <param name="Location">
/// The item's bin location in the configured warehouse, or "" when it has none. Resolved at the
/// client boundary so no layer above Integration learns SkuLabs reports a per-warehouse map.
/// <para>
/// <c>null</c> means locations were not requested at all because no warehouse is configured — a
/// distinct thing from "" ("we asked, this item has no location"). Only the latter is grounds for
/// clearing a stored location.
/// </para>
/// </param>
public readonly record struct SkulabsApiItem(
    string SourceItemId,
    string Name,
    string Sku,
    string Upc,
    string? Location,
    IReadOnlyList<SkulabsApiListing> Listings);
