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
}
