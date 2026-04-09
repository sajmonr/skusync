namespace Application.Events;

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
/// A lightweight, in-process event emitted whenever a product variant is created or updated
/// in the local database. Instances are accumulated in <see cref="IProductEventAccumulator"/>
/// and processed in batches by <c>ProductEventProcessorJob</c>.
/// </summary>
/// <param name="VariantId">The numeric Shopify variant ID of the affected variant.</param>
/// <param name="ChangeType">Whether the variant was created or updated.</param>
public record ProductChangedEvent(long VariantId, ProductChangeType ChangeType);
