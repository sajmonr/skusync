namespace Integration.Shopify.Products;

public readonly record struct ShopifyProductVariant(
    string GlobalProductId,
    string GlobalVariantId,
    string Title,
    string Sku,
    string Barcode)
{

    public long ProductId { get; } = GetIdOrDefault(GlobalProductId);

    public long VariantId { get; } = GetIdOrDefault(GlobalVariantId);

    private static long GetIdOrDefault(string id)
    {
        if (long.TryParse(id[(id.LastIndexOf('/') + 1)..], out var longId))
        {
            return longId;
        }
        
        return 0;
    }
    
}