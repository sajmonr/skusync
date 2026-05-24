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
    /// Updates an existing SkuLabs item with the supplied fields.
    /// </summary>
    /// <param name="itemId">SkuLabs item identifier (<c>_id</c>) to update.</param>
    /// <param name="update">New representation of the item.</param>
    Task UpdateItem(string itemId, SkulabsItemUpdate update);
}
