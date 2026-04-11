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
}
