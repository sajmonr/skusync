namespace Application.Skulabs.Services;

/// <summary>
/// Outcome of a single <see cref="IShopifyVariantDriftSyncService"/> run.
/// </summary>
/// <param name="Checked">Number of linked SkuLabs items examined for drift.</param>
/// <param name="Drifted">Subset of <paramref name="Checked"/> whose Shopify variant SKU or barcode differed from the SkuLabs values.</param>
/// <param name="Corrected">Number of variants whose Shopify SKU/barcode was successfully overwritten with the SkuLabs values.</param>
/// <param name="Failed">Number of variants where the Shopify update returned a failure; their local row was left untouched and will be retried on the next run.</param>
public readonly record struct ShopifyVariantDriftSyncResult(
    int Checked,
    int Drifted,
    int Corrected,
    int Failed)
{
    public static ShopifyVariantDriftSyncResult Empty => new(0, 0, 0, 0);
}
