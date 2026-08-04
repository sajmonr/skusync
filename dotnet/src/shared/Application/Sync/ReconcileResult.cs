namespace Application.Sync;

/// <summary>
/// Outcome of a single <see cref="IReconciler"/> run.
/// </summary>
/// <param name="VariantsMarked">Variants whose SKU/barcode was mirrored from SkuLabs and marked pending a Shopify push.</param>
/// <param name="ItemsMarked">SkuLabs items whose title was mirrored from the variant and marked pending a SkuLabs push.</param>
public readonly record struct ReconcileResult(int VariantsMarked, int ItemsMarked)
{
    public static ReconcileResult Empty => new(0, 0);
}
