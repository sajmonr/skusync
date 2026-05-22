namespace Infrastructure.Database.Entities;

public class SkulabsItemEntity
{

    public Guid SkulabsItemId { get; set; }  = Guid.CreateVersion7();

    public Guid ShopifyProductVariantId { get; set; } = Guid.Empty;
    
    public string SkulabsSourceItemId { get; set; } = string.Empty;

    public string SkulabsSourceListingId { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Sku { get; set; } = string.Empty;

    public string Barcode { get; set; } = string.Empty;

    public ShopifyProductVariantEntity? ShopifyProductVariant { get; set; }
    
}