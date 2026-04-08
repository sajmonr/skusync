namespace Integration.Skulabs.Items;

/// <summary>
/// Represents a SkuLabs inventory item mapped from the SkuLabs API response,
/// enriched with the linked Shopify variant and product identifiers.
/// </summary>
public record SkuLabsItem(
    /// <summary>The internal SkuLabs item identifier.</summary>
    string SkulabsId,
    /// <summary>The Shopify variant ID taken from the item's first listing.</summary>
    string ShopifyVariantId,
    /// <summary>The Shopify product ID taken from the item's first listing.</summary>
    string ShopifyProductId,
    /// <summary>The stock-keeping unit (SKU) assigned to this item in SkuLabs.</summary>
    string Sku,
    /// <summary>The barcode (UPC/EAN) assigned to this item in SkuLabs.</summary>
    string Barcode)
{
    internal static SkuLabsItem FromResponse(SkulabsItemResponse response)
    {
        return new SkuLabsItem(response.Id, response.Listings[0].VariantId, response.Listings[0].ProductId,
            response.Sku, response.Upc);
    }
}
