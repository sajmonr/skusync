namespace Application.Sync.Merge.Rules;

/// <summary>
/// Decides a variant's barcode, on the same authority model as the SKU: SkuLabs' value is
/// physically materialized and so outranks everything, and a value we already decided stands
/// against Shopify drift.
/// <para>
/// Where the two differ is the fallback. There is no barcode generator — a barcode has no
/// human-readable structure to derive — so a variant that reaches us without one takes the Shopify
/// variant id. It is unique, stable, and numeric, which is all a scannable code has to be.
/// </para>
/// </summary>
public sealed class BarcodeMergeRule : IMergeRule
{
    public IReadOnlyCollection<ItemField> OwnedFields { get; } = [ItemField.Barcode];

    public ValueTask Apply(MergeContext context, CancellationToken cancellationToken = default)
    {
        if (context.Skulabs.Barcode.HasValue)
        {
            context.Result.Barcode = context.Skulabs.Barcode.Value;
            return ValueTask.CompletedTask;
        }

        if (context.Result.Barcode.Length > 0)
        {
            return ValueTask.CompletedTask;
        }

        // As with the SKU, a first sighting on a webhook is the only case that distrusts the
        // payload's barcode, presuming it to be a duplicated product's leftovers.
        if (context.Origin != MergeOrigin.WebhookCreate && context.Shopify.Barcode.HasValue)
        {
            context.Result.Barcode = context.Shopify.Barcode.Value;
            return ValueTask.CompletedTask;
        }

        if (context.Origin == MergeOrigin.WebhookCreate)
        {
            context.Result.Barcode = context.ShopifyVariantId.ToString();
        }

        return ValueTask.CompletedTask;
    }
}
