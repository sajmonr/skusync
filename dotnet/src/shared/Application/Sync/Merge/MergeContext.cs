namespace Application.Sync.Merge;

/// <summary>
/// Everything a merge rule is allowed to see: what each external system last reported, what we
/// currently believe, and a result to write into.
/// <para>
/// Deliberately expressed in neutral field names rather than in entity or API types. The same
/// concept is called <c>DisplayName</c> on a Shopify variant and <c>Title</c> on a SkuLabs item;
/// without a shared vocabulary the rules would be written twice, once per direction, which is the
/// duplication this whole mechanism exists to remove.
/// </para>
/// </summary>
public sealed class MergeContext
{
    public MergeContext(
        MergeOrigin origin,
        long shopifyVariantId,
        string productTitle,
        string variantTitle,
        ItemObservation shopify,
        ItemObservation skulabs,
        MergeResult result,
        ISet<string> reservedSkus)
    {
        Origin = origin;
        ShopifyVariantId = shopifyVariantId;
        ProductTitle = productTitle;
        VariantTitle = variantTitle;
        Shopify = shopify;
        Skulabs = skulabs;
        Result = result;
        ReservedSkus = reservedSkus;
    }

    /// <summary>What caused this merge; see <see cref="MergeOrigin"/> for why rules care.</summary>
    public MergeOrigin Origin { get; }

    /// <summary>
    /// Shopify's numeric variant id. Used as a last-resort code by rules that must produce a value
    /// with nothing to derive one from, and as a stable fallback segment for SKU generation.
    /// </summary>
    public long ShopifyVariantId { get; }

    /// <summary>
    /// The product and variant titles as Shopify sent them, kept apart rather than composed.
    /// SKU generation abbreviates each separately, and a composed title cannot be split back apart
    /// reliably — either part may contain brackets of its own.
    /// </summary>
    public string ProductTitle { get; }

    /// <inheritdoc cref="ProductTitle"/>
    public string VariantTitle { get; }

    /// <summary>What Shopify last told us this variant holds.</summary>
    public ItemObservation Shopify { get; }

    /// <summary>
    /// What SkuLabs last told us the linked item holds. Entirely unobserved when the variant has no
    /// usable link — no item, an ambiguous item, or a variant claimed by more than one item.
    /// </summary>
    public ItemObservation Skulabs { get; }

    /// <summary>
    /// The values being decided, seeded from what is currently stored. Reads return the running
    /// decision, so a later rule sees what an earlier one chose.
    /// </summary>
    public MergeResult Result { get; }

    /// <summary>
    /// SKUs already handed out in this pass but not yet committed. Without it two variants merged in
    /// the same batch could be issued the same generated SKU, since neither is in the database for
    /// the other's uniqueness check to find.
    /// </summary>
    public ISet<string> ReservedSkus { get; }
}

/// <summary>One external system's view of an item, field by field.</summary>
/// <param name="Sku">The stock-keeping unit as that system holds it.</param>
/// <param name="Barcode">The barcode/UPC as that system holds it.</param>
/// <param name="Title">The item's name as that system holds it.</param>
/// <param name="Location">The bin location, only ever reported by SkuLabs.</param>
public readonly record struct ItemObservation(
    ObservedValue Sku,
    ObservedValue Barcode,
    ObservedValue Title,
    ObservedValue Location)
{
    /// <summary>Nothing heard from this system at all — the shape a missing SkuLabs link takes.</summary>
    public static readonly ItemObservation None = new(
        ObservedValue.Unobserved,
        ObservedValue.Unobserved,
        ObservedValue.Unobserved,
        ObservedValue.Unobserved);
}
