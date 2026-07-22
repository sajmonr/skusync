namespace Infrastructure.Database.Entities;

public class ShopifyProductVariantLogEventEntity
{

    public Guid ShopifyProductVariantLogEventId { get; set; } = Guid.CreateVersion7();

    public Guid ShopifyProductVariantId { get; set; }
    
    public DateTime CreatedOn { get; set; } = DateTime.UtcNow;

    public string Message { get; set; } = "";

    public ShopifyProductVariantEntity? ShopifyProductVariant { get; set; }
    
}