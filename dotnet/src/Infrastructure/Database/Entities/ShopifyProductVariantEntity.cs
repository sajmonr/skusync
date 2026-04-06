namespace Infrastructure.Database.Entities;

public class ShopifyProductVariantEntity
{

    private string _fullTitle = "";
    
    public Guid ShopifyProductVariantId { get; set; } = Guid.CreateVersion7();

    public string GlobalProductId { get; set; } = "";

    public long ProductId { get; set; }

    public string GlobalVariantId { get; set; } = "";

    public long VariantId { get; set; }

    public string ProductTitle
    {
        get;
        set
        {
            field = value;
            GenerateFullTitle();
        }
    } = "";

    public string VariantTitle
    {
        get;
        set
        {
            if (value == "Default Title")
            {
                field = "";
                return;
            }
            
            field = value;
            GenerateFullTitle();
        }
    } = "";

    public string FullTitle
    {
        get => _fullTitle;
        init => _fullTitle = value;
    }

    public string Sku { get; set; } = "";
    
    public string Barcode { get; set; } = "";

    public DateTime CreatedOnUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedOnUtc { get; set; } = DateTime.UtcNow;
 
    private void GenerateFullTitle()
    {
        _fullTitle = string.IsNullOrWhiteSpace(VariantTitle) ? ProductTitle : $"{ProductTitle} ({VariantTitle})";
    }
    
}