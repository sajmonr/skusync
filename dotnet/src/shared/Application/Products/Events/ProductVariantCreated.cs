namespace Application.Products.Events;

public readonly record struct ProductVariantCreatedEvent(Guid ProductVariantId);
