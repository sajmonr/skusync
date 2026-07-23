namespace Integration.Shopify.Products;

/// <summary>
/// Represents an immutable data structure for updating a Shopify product variant.
/// </summary>
/// <remarks>
/// This record is used to encapsulate the necessary variant details when updating variants
/// for a specific Shopify product. It includes the global variant ID, SKU (Stock Keeping Unit),
/// and barcode for the variant. This ensures that the variant update operation is performed
/// with a clear and consistent data structure.
/// </remarks>
/// <param name="GlobalVariantId">
/// The globally unique identifier for the Shopify product variant. This identifier
/// is used to specify which variant is to be updated.
/// </param>
/// <param name="Sku">
/// The stock keeping unit (SKU) for the variant. This value is used to uniquely identify
/// and manage inventory for the product variant.
/// </param>
/// <param name="Barcode">
/// The barcode associated with the variant. This is typically used for inventory
/// tracking and scanning purposes.
/// </param>
public readonly record struct ShopifyUpdateProductVariant(string GlobalVariantId, string Sku, string Barcode);