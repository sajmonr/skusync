namespace Integration.Skulabs.Items;

/// <summary>
/// Abstraction over the SkuLabs Items API client to enable substitution in tests.
/// </summary>
public interface ISkulabsItemClient
{
    /// <summary>
    /// Fetches every SkuLabs inventory item with all of its channel listings intact. Nothing is
    /// dropped or flattened; the caller classifies items via <see cref="SkulabsItemCollection"/>.
    /// </summary>
    Task<SkulabsItemCollection> GetAllItems();

    /// <summary>
    /// Updates one or more SkuLabs items in a single call via <c>PUT /item/bulk_upsert</c>.
    /// </summary>
    /// <param name="updates">Items to update, each identified by its SkuLabs id.</param>
    Task UpdateItems(IEnumerable<SkulabsItemUpdateWithId> updates);
}
