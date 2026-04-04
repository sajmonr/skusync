namespace Infrastructure.Database.Entities;

public class ShopifyProductVariantEntity
{

    public Guid ShopifyProductVariantId { get; set; }

    public string GlobalProductId { get; set; } = "";

    public long ProductId { get; set; }

    public string GlobalVariantId { get; set; } = "";

    public long VariantId { get; set; }

    public string Title { get; set; } = "";
    
    public string Sku { get; set; } = "";
    
    public string Barcode { get; set; } = "";

    public DateTime CreatedOnUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedOnUtc { get; set; } = DateTime.UtcNow;
    
}