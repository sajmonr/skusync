namespace Application.Products.Events;

/// <summary>
/// Indicates whether a product variant was newly added to the local database
/// or an existing record was modified.
/// </summary>
public enum ProductChangeType
{
    /// <summary>The variant was inserted into the local database for the first time.</summary>
    Created,

    /// <summary>An existing variant record in the local database was updated.</summary>
    Updated
}

/// <summary>
/// Represents an event that captures changes to a product variant in the system.
/// </summary>
/// <remarks>
/// This event is used to identify whether a product variant was either newly added
/// to the local database or an existing record was modified. It encapsulates the
/// unique identifier of the product variant involved and the type of change that occurred.
/// </remarks>
public readonly record struct ProductChangedEvent(Guid ProductVariantId, ProductChangeType ChangeType)
{
    /// <summary>
    /// Creates a new instance of the <see cref="ProductChangedEvent"/> indicating
    /// that a product variant was newly added to the local database.
    /// </summary>
    /// <param name="productVariantId">
    /// The unique identifier of the product variant that was created.
    /// Must not be an empty GUID.
    /// </param>
    /// <returns>
    /// A <see cref="ProductChangedEvent"/> instance representing the 'Created' change type
    /// for the specified product variant.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown if the <paramref name="productVariantId"/> is an empty GUID.
    /// </exception>
    public static ProductChangedEvent Created(Guid productVariantId)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(Guid.Empty, productVariantId);
        
        return new(productVariantId, ProductChangeType.Created);
    }

    /// <summary>
    /// Creates a new instance of the <see cref="ProductChangedEvent"/> indicating
    /// that an existing product variant record was modified in the local database.
    /// </summary>
    /// <param name="productVariantId">
    /// The unique identifier of the product variant that was updated.
    /// Must not be an empty GUID.
    /// </param>
    /// <returns>
    /// A <see cref="ProductChangedEvent"/> instance representing the 'Updated' change type
    /// for the specified product variant.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown if the <paramref name="productVariantId"/> is an empty GUID.
    /// </exception>
    public static ProductChangedEvent Updated(Guid productVariantId)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(Guid.Empty, productVariantId);
        
        return new(productVariantId, ProductChangeType.Updated);
    }
}
