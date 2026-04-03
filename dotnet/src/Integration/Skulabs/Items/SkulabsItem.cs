namespace Integration.Skulabs.Items;

public record SkuLabsItem(
    string SkulabsId,
    string ShopifyVariantId,
    string ShopifyProductId,
    string Sku,
    string Barcode)
{
    internal static SkuLabsItem FromResponse(SkulabsItemResponse response)
    {
        return new SkuLabsItem(response.Id, response.Listings[0].VariantId, response.Listings[0].ProductId,
            response.Sku, response.Upc);
    }
}
