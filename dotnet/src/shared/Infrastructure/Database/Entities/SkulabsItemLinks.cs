using System.Linq.Expressions;

namespace Infrastructure.Database.Entities;

/// <summary>
/// The single definition of when a SkuLabs listing counts as a usable link between a SkuLabs item and
/// a Shopify variant. Every read path that treats a variant as "having" a SkuLabs item — or an item as
/// "having" a variant — must apply this, because nothing in the schema enforces it.
/// </summary>
public static class SkulabsItemLinks
{
    /// <summary>
    /// A link is syncable only when it resolves to a variant we hold <em>and</em> it is the sole
    /// listing on both sides.
    /// <para>
    /// The variant side of that check is the one that is easy to miss and expensive to get wrong.
    /// The reconciler writes the variant's SKU and barcode from the item and the item's title from
    /// the variant; if two SkuLabs items were allowed to link to one variant, "which SKU wins" would
    /// be undefined and both items would flip <c>PendingSkulabsSync</c> on every pass, pushing to
    /// SkuLabs forever. A unique foreign-key index used to make that unrepresentable — now it is this
    /// predicate's job.
    /// </para>
    /// </summary>
    public static readonly Expression<Func<SkulabsItemListingEntity, bool>> IsSyncable =
        listing => listing.ShopifyProductVariantId != null
                   && listing.SkulabsItem!.Listings.Count == 1
                   && listing.ShopifyProductVariant!.SkulabsItemListings.Count == 1;

    /// <summary>
    /// The same rule read from the variant end: pairs each variant with the SkuLabs item it resolves
    /// to, or null when it has no listing, several listings, or a single listing belonging to an
    /// ambiguous item.
    /// <para>
    /// Read paths project through this before filtering or sorting so the rule is stated once. Writing
    /// it out at each call site instead would duplicate a correctness-critical predicate across every
    /// grid filter, sort map and endpoint projection, where a single omission silently resurrects the
    /// contested-variant bug.
    /// </para>
    /// </summary>
    /// <remarks>
    /// Shaped as a <c>SelectMany</c> + <c>DefaultIfEmpty</c> — a left join — rather than a
    /// conditional subquery in the projection. The conditional form reads more directly but produces
    /// a subquery returning an entity, which neither provider can push into a <c>WHERE</c> when a
    /// caller then filters on the resolved item.
    /// </remarks>
    public static IQueryable<VariantWithSkulabsItem> WithResolvedSkulabsItem(
        this IQueryable<ShopifyProductVariantEntity> variants) =>
        variants.SelectMany(
            variant => variant.SkulabsItemListings
                .Where(listing => variant.SkulabsItemListings.Count == 1
                                  && listing.SkulabsItem!.Listings.Count == 1)
                .Select(listing => listing.SkulabsItem)
                .DefaultIfEmpty(),
            (variant, skulabsItem) => new VariantWithSkulabsItem
            {
                Variant = variant,
                SkulabsItem = skulabsItem
            });
}
