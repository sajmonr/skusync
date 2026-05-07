namespace Integration.Skulabs.Items;

/// <summary>
/// Represents a SkuLabs inventory item mapped from the SkuLabs API response,
/// enriched with the linked Shopify variant and product identifiers.
/// </summary>
public record SkuLabsItem(
    string SkulabsItemId,
    string SkulabsListingId,
    string ShopifyVariantId,
    string Sku,
    string Barcode,
    string Title)
{
    /// <summary>
    /// Converts a <see cref="SkulabsItemResponse"/> object to a <see cref="SkuLabsItem"/> domain model instance.
    /// This method maps response fields, ensuring compatibility between the API response and the application's domain model.
    /// </summary>
    /// <param name="response">
    /// The <see cref="SkulabsItemResponse"/> object representing the raw API response for a SkuLabs inventory item.
    /// </param>
    /// <returns>
    /// A <see cref="SkuLabsItem"/> instance populated with the mapped data from the provided API response.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the <paramref name="response"/> contains multiple or no listings, as each SkuLabs item must have exactly one associated listing.
    /// </exception>
    internal static SkuLabsItem FromResponse(SkulabsItemResponse response)
    {
        if (response.Listings.Length != 1)
        {
            throw new InvalidOperationException("SkuLabs item must have exactly one listing associated with it.");
        }

        return new SkuLabsItem(response.ItemId, response.Listings[0].ListingId, response.Listings[0].VariantId,
            response.Sku, response.Upc, response.Title);
    }
}