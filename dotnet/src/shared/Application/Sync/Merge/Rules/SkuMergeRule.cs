using Application.Skus;
using Microsoft.Extensions.Logging;

namespace Application.Sync.Merge.Rules;

/// <summary>
/// Decides a variant's SKU.
/// <para>
/// <b>SkuLabs wins whenever it has one.</b> Not because SkuLabs is more authoritative in the
/// abstract, but because its codes are <em>physically materialized</em> — printed onto labels stuck
/// to stock that gets picked and shipped. A code we prefer over one already on a label makes that
/// stock unscannable, so whatever SkuLabs holds is accepted verbatim, including a value an operator
/// typed by hand.
/// </para>
/// <para>
/// Generation is therefore a <em>bid</em>, not an assertion: it exists to get a structured code into
/// Shopify before SkuLabs' own sync copies whatever is there and freezes it. Losing that race costs
/// nothing but tidiness — the system converges on the materialized code either way.
/// </para>
/// </summary>
public sealed class SkuMergeRule(ISkuGenerator skuGenerator, ILogger<SkuMergeRule> logger) : IMergeRule
{
    public IReadOnlyCollection<ItemField> OwnedFields { get; } = [ItemField.Sku];

    public async ValueTask Apply(MergeContext context, CancellationToken cancellationToken = default)
    {
        if (context.Skulabs.Sku.HasValue)
        {
            context.Result.Sku = context.Skulabs.Sku.Value;
            return;
        }

        // A SKU we already decided stands. Shopify drifting away from it is precisely the divergence
        // the dispatcher exists to correct, so taking Shopify's value here would make the two sides
        // agree by surrendering rather than by pushing.
        if (context.Result.Sku.Length > 0)
        {
            return;
        }

        // A first sighting on a webhook is the one case where a payload SKU is distrusted: the
        // usual way a variant appears that way is a merchant duplicating a product without clearing
        // its codes, so the SKU is presumed to be the original's and gets replaced. Every other
        // path honours it, because a SKU regenerated later would not match the one generated when
        // the variant was created — the product may since have been renamed, and the SKU derives
        // from the name.
        if (context.Origin != MergeOrigin.WebhookCreate && context.Shopify.Sku.HasValue)
        {
            context.Result.Sku = context.Shopify.Sku.Value;
            return;
        }

        var generated = await skuGenerator.Generate(
            context.ProductTitle,
            context.VariantTitle,
            context.ReservedSkus,
            fallbackSegment: context.ShopifyVariantId.ToString(),
            cancellationToken);

        context.ReservedSkus.Add(generated);
        context.Result.Sku = generated;

        logger.LogInformation(
            "Generated SKU '{Sku}' for Shopify variant {VariantId} ({Origin}).",
            generated, context.ShopifyVariantId, context.Origin);
    }
}
