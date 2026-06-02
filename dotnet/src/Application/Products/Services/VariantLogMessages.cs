namespace Application.Products.Services;

/// <summary>
/// Provides factory methods for producing consistent, human-readable log event messages
/// describing changes to Shopify product variants.
/// </summary>
internal static class VariantLogMessages
{
    private const string VariantCreatedMessage = "Product variant was created.";
    
    /// <summary>Returns a message indicating that a product variant was added to the system.</summary>
    public static string VariantCreated() => VariantCreatedMessage;

    /// <summary>Returns a message indicating that the variant's display title changed.</summary>
    /// <param name="oldTitle">The previous full title (e.g. "Blue T-Shirt (Large)").</param>
    /// <param name="newTitle">The updated full title.</param>
    public static string TitleUpdated(string oldTitle, string newTitle) =>
        $"Title changed from '{oldTitle}' to '{newTitle}'.";

    /// <summary>Returns a message indicating that a SKU was assigned to a variant that previously had none.</summary>
    /// <param name="sku">The newly assigned SKU value.</param>
    public static string SkuSet(string sku) => $"SKU assigned: '{sku}'.";

    /// <summary>Returns a message indicating that an existing SKU was replaced with a new value.</summary>
    /// <param name="oldSku">The previous SKU value.</param>
    /// <param name="newSku">The replacement SKU value.</param>
    public static string SkuUpdated(string oldSku, string newSku) =>
        $"SKU changed from '{oldSku}' to '{newSku}'.";

    /// <summary>Returns a message indicating that a barcode was assigned to a variant that previously had none.</summary>
    /// <param name="barcode">The newly assigned barcode value.</param>
    public static string BarcodeSet(string barcode) => $"Barcode assigned: '{barcode}'.";

    /// <summary>Returns a message indicating that an existing barcode was replaced with a new value.</summary>
    /// <param name="oldBarcode">The previous barcode value.</param>
    /// <param name="newBarcode">The replacement barcode value.</param>
    public static string BarcodeUpdated(string oldBarcode, string newBarcode) =>
        $"Barcode changed from '{oldBarcode}' to '{newBarcode}'.";

    /// <summary>Returns a message indicating that a SkuLabs item was newly linked to this variant.</summary>
    /// <param name="skulabsItemId">The SkuLabs source item id of the newly linked item.</param>
    public static string SkulabsLinked(string skulabsItemId) =>
        $"Linked to SkuLabs item '{skulabsItemId}'.";

    /// <summary>Returns a message indicating that the SkuLabs item previously linked to this
    /// variant is no longer linked (either because it moved to a different variant or because a
    /// different SkuLabs item now claims this variant).</summary>
    /// <param name="skulabsItemId">The SkuLabs source item id that was unlinked.</param>
    public static string SkulabsUnlinked(string skulabsItemId) =>
        $"Unlinked from SkuLabs item '{skulabsItemId}'.";

    /// <summary>Returns a message indicating that the variant's SKU was corrected in Shopify to
    /// match the authoritative value held by the linked SkuLabs item.</summary>
    /// <param name="oldSku">The drifted Shopify SKU value that was replaced.</param>
    /// <param name="newSku">The SkuLabs SKU now written to Shopify.</param>
    public static string SkuCorrectedFromSkulabs(string oldSku, string newSku) =>
        $"SKU corrected to match SkuLabs: '{oldSku}' → '{newSku}'.";

    /// <summary>Returns a message indicating that the variant's barcode was corrected in Shopify
    /// to match the authoritative value held by the linked SkuLabs item.</summary>
    /// <param name="oldBarcode">The drifted Shopify barcode value that was replaced.</param>
    /// <param name="newBarcode">The SkuLabs barcode now written to Shopify.</param>
    public static string BarcodeCorrectedFromSkulabs(string oldBarcode, string newBarcode) =>
        $"Barcode corrected to match SkuLabs: '{oldBarcode}' → '{newBarcode}'.";

    /// <summary>Returns a message indicating that the linked SkuLabs item's title was corrected
    /// in SkuLabs to match the authoritative display name held on this variant.</summary>
    /// <param name="oldTitle">The previous SkuLabs item title that was replaced.</param>
    /// <param name="newTitle">The variant display name now written to SkuLabs.</param>
    public static string SkulabsTitleSyncedFromVariant(string oldTitle, string newTitle) =>
        $"SkuLabs item title corrected to match variant: '{oldTitle}' → '{newTitle}'.";

    /// <summary>Returns a message indicating that the variant was deactivated after too many
    /// consecutive failed attempts to push a SkuLabs-driven correction to Shopify.</summary>
    /// <param name="failedAttempts">The number of consecutive failed Shopify push attempts.</param>
    public static string DeactivatedAfterFailedShopifySyncs(int failedAttempts) =>
        $"Variant deactivated after {failedAttempts} consecutive failed Shopify sync attempts.";

    /// <summary>Returns a message indicating that a previously-deactivated variant was revived
    /// because Shopify sent a fresh <c>products/update</c> webhook for it.</summary>
    public static string Reactivated() =>
        "Variant reactivated after a Shopify products/update webhook.";
}
