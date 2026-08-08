namespace Application.Sync.Merge.Rules;

/// <summary>
/// Decides the title the linked SkuLabs item should carry: Shopify's, always.
/// <para>
/// The direction is the exact opposite of the SKU rule's, and against the same system — worth
/// stating plainly, because the asymmetry looks like an inconsistency until you know the criterion.
/// It is not about which system is more authoritative; it is about whether the value is physically
/// materialized. SkuLabs titles are system reference only, never printed onto anything a picker
/// relies on, so overwriting one costs nothing. Overwriting a barcode strands tagged stock.
/// </para>
/// </summary>
public sealed class TitleMergeRule : IMergeRule
{
    public IReadOnlyCollection<ItemField> OwnedFields { get; } = [ItemField.Title];

    public ValueTask Apply(MergeContext context, CancellationToken cancellationToken = default)
    {
        if (context.Shopify.Title.IsObserved)
        {
            context.Result.Title = context.Shopify.Title.Value;
        }

        return ValueTask.CompletedTask;
    }
}
