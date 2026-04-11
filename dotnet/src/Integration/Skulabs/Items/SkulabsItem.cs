namespace Integration.Skulabs.Items;

/// <summary>
/// Represents a SkuLabs inventory item mapped from the SkuLabs API response,
/// enriched with the linked Shopify variant and product identifiers.
/// </summary>
public record SkuLabsItem(
    string SkulabsId,
    string ShopifyVariantId,
    string ShopifyProductId,
    string Sku,
    string Barcode)
{
    /// <summary>
    /// Converts a <see cref="SkulabsItemResponse"/> object to a <see cref="SkuLabsItem"/> domain model instance.
    /// This method maps response fields, ensuring compatibility between the API data and the application model.
    /// </summary>
    /// <param name="response">
    /// The <see cref="SkulabsItemResponse"/> object representing the raw API response for a SkuLabs inventory item.
    /// </param>
    /// <returns>
    /// A <see cref="SkuLabsItem"/> object containing the mapped and enriched domain model representation of the item.
    /// </returns>
    internal static SkuLabsItem FromResponse(SkulabsItemResponse response)
    {
        return new SkuLabsItem(response.Id, response.Listings[0].VariantId, response.Listings[0].ProductId,
            response.Sku, response.Upc);
    }
}
