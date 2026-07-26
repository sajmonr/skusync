using SharedKernel;

namespace Integration.Skulabs.Items;

/// <summary>
/// The full set of items returned by <see cref="ISkulabsItemClient.GetAllItems"/>. Fetching returns
/// <em>everything</em>; deciding what can be synced is the caller's concern, exposed here as
/// <see cref="GetSyncable"/> and <see cref="GetAmbiguous"/>. An item is syncable only when it has
/// exactly one listing whose variant id is a Shopify (numeric) variant; everything else is ambiguous.
/// </summary>
public sealed class SkulabsItemCollection(IReadOnlyList<SkulabsApiItem> items)
{
    /// <summary>Every item SkuLabs returned, with all listings intact.</summary>
    public IReadOnlyList<SkulabsApiItem> Items { get; } = items;

    /// <summary>
    /// The items that map cleanly to a single Shopify variant, projected to the flat shape the
    /// active reconciler consumes.
    /// </summary>
    public IReadOnlyList<SkuLabsItem> GetSyncable()
    {
        var syncable = new List<SkuLabsItem>(Items.Count);
        foreach (var item in Items)
        {
            if (item.Listings.Count != 1)
            {
                continue;
            }

            var listing = item.Listings[0];
            if (!long.TryParse(listing.RawVariantId, out var variantId))
            {
                continue;
            }

            syncable.Add(new SkuLabsItem(
                item.SourceItemId,
                listing.ListingId,
                variantId,
                item.Sku,
                item.Upc,
                item.Name));
        }

        return syncable;
    }

    /// <summary>
    /// The items that do not fit the strict single-Shopify-listing criterion, each tagged with the
    /// reason it was quarantined and carrying all of its listings for later examination.
    /// </summary>
    public IReadOnlyList<SkulabsAmbiguousItem> GetAmbiguous()
    {
        var ambiguous = new List<SkulabsAmbiguousItem>();
        foreach (var item in Items)
        {
            if (!TryClassifyAmbiguity(item, out var reason))
            {
                continue;
            }

            ambiguous.Add(new SkulabsAmbiguousItem(
                item.SourceItemId,
                item.Name,
                item.Sku,
                item.Upc,
                reason,
                item.Listings));
        }

        return ambiguous;
    }

    /// <summary>
    /// Determines whether <paramref name="item"/> is ambiguous and, if so, why. Returns
    /// <c>false</c> for a cleanly syncable item (single numeric-variant listing).
    /// </summary>
    private static bool TryClassifyAmbiguity(SkulabsApiItem item, out SkulabsAmbiguityReason reason)
    {
        switch (item.Listings.Count)
        {
            case 0:
                reason = SkulabsAmbiguityReason.NoListings;
                return true;
            case > 1:
                reason = SkulabsAmbiguityReason.MultipleListings;
                return true;
            default:
                if (!long.TryParse(item.Listings[0].RawVariantId, out _))
                {
                    reason = SkulabsAmbiguityReason.ListingNotInShopify;
                    return true;
                }

                reason = default;
                return false;
        }
    }
}
