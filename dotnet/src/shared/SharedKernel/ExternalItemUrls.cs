namespace SharedKernel;

public static class ExternalItemUrls
{
    public static string CreateShopifyProductUrl(long productId, long variantId) =>
        $"https://admin.shopify.com/store/ivyandlavyboutique/products/{productId}/variants/{variantId}";

    public static string CreateSkulabsItemUrl(string skulabsItemId) =>
        $"https://app.skulabs.com/item?id={Uri.EscapeDataString(skulabsItemId)}";
}
