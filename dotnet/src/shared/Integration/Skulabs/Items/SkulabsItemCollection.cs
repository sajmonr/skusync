namespace Integration.Skulabs.Items;

/// <summary>
/// The set of items returned by <see cref="ISkulabsItemClient.GetAllItems"/>, with every non-Shopify
/// listing stripped on construction — a listing counts only when its variant id is a Shopify (numeric)
/// id; a null, empty or otherwise non-numeric id is an internal SkuLabs variant that can never match a
/// Shopify variant. Deciding what to do with each item is the caller's concern, exposed as
/// <see cref="GetSyncable"/>, <see cref="GetAmbiguous"/> and
/// <see cref="GetSourceItemIdsWithoutShopifyListings"/>. An item is syncable when it has exactly one
/// Shopify listing, ambiguous when it has more than one, and left alone when it has none.
/// </summary>
public sealed class SkulabsItemCollection
{
    public SkulabsItemCollection(IReadOnlyList<SkulabsApiItem> items) =>
        Items = items.Select(WithShopifyListingsOnly).ToArray();

    /// <summary>Every item SkuLabs returned, with only its Shopify (numeric-variant) listings retained.</summary>
    public IReadOnlyList<SkulabsApiItem> Items { get; }

    private static SkulabsApiItem WithShopifyListingsOnly(SkulabsApiItem item) =>
        item with
        {
            Listings = item.Listings
                .Where(listing => long.TryParse(listing.RawVariantId, out _))
                .ToArray()
        };

    /// <summary>
    /// The items that map to a single Shopify variant, projected to the flat shape the active
    /// reconciler consumes.
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
    /// The items that map to more than one Shopify listing. The target variant is ambiguous, so these
    /// are quarantined for review rather than synced. Each carries all of its Shopify listings —
    /// including any pointing at a Shopify variant that has since been deleted — so a reviewer can see
    /// exactly what to fix in SkuLabs.
    /// </summary>
    public IReadOnlyList<SkulabsAmbiguousItem> GetAmbiguous()
    {
        var ambiguous = new List<SkulabsAmbiguousItem>();
        foreach (var item in Items)
        {
            if (item.Listings.Count <= 1)
            {
                continue;
            }

            ambiguous.Add(new SkulabsAmbiguousItem(
                item.SourceItemId,
                item.Name,
                item.Sku,
                item.Upc,
                item.Listings));
        }

        return ambiguous;
    }

    /// <summary>
    /// The source ids of items that have no Shopify listing at all — someone created a SkuLabs-only
    /// item that is not (or no longer) in Shopify. There is nothing to sync, so the reconciler leaves
    /// them out of both tables and severs any link they may still carry from a previous run.
    /// </summary>
    public IReadOnlyList<string> GetSourceItemIdsWithoutShopifyListings()
    {
        var sourceItemIds = new List<string>();
        foreach (var item in Items)
        {
            if (item.Listings.Count == 0)
            {
                sourceItemIds.Add(item.SourceItemId);
            }
        }

        return sourceItemIds;
    }
}
