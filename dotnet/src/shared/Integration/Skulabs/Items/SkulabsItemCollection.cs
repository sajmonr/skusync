namespace Integration.Skulabs.Items;

/// <summary>
/// The set of items returned by <see cref="ISkulabsItemClient.GetAllItems"/>, with every non-Shopify
/// listing stripped on construction — a listing counts only when its variant id is a Shopify (numeric)
/// id; a null, empty or otherwise non-numeric id is an internal SkuLabs variant that can never match a
/// Shopify variant.
/// <para>
/// That is the only policy applied here. How many Shopify listings an item ends up with — none, one,
/// or several — is left for the caller to interpret, because it is what decides whether the item is
/// syncable, SkuLabs-only, or ambiguous.
/// </para>
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
}
