namespace Integration.Skulabs.Items;

/// <summary>
/// Abstraction over the SkuLabs Items API client to enable substitution in tests.
/// </summary>
public interface ISkulabsItemClient
{
    /// <summary>
    /// Fetches all SkuLabs inventory items that have exactly one Shopify channel listing.
    /// </summary>
    Task<SkuLabsItem[]> GetAllItems();

    /// <summary>
    /// Updates one or more SkuLabs items in a single call via <c>PUT /item/bulk_upsert</c>.
    /// </summary>
    /// <param name="updates">Items to update, each identified by its SkuLabs id.</param>
    Task UpdateItems(IEnumerable<SkulabsItemUpdateWithId> updates);
}
