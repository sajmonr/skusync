using Infrastructure.Database.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Database.Configuration;

public class ShopifyProductVariantConfiguration : IEntityTypeConfiguration<ShopifyProductVariantEntity>
{
    public void Configure(EntityTypeBuilder<ShopifyProductVariantEntity> builder)
    {
        builder.ToTable("ShopifyProductVariant");

        builder.HasUuidV7PrimaryKey(x => x.ShopifyProductVariantId);

        builder.Property(x => x.GlobalProductId)
            .IsRequired()
            .HasMaxLength(255);
        builder.Property(x => x.GlobalVariantId).IsRequired().HasMaxLength(255);
        
        builder.Property(x => x.ProductTitle).IsRequired().HasMaxLength(400);
        builder.Property(x => x.VariantTitle).IsRequired().HasMaxLength(200);
        builder.Property(x => x.FullTitle).IsRequired().HasMaxLength(1000);

        builder.Property(x => x.ProductId).IsRequired();
        builder.Property(x => x.VariantId).IsRequired();

        builder.Property(x => x.CreatedOnUtc)
            .IsRequired()
            .HasDefaultValueDateTimeNowUtcSql();
        
        builder.Property(x => x.UpdatedOnUtc)
            .IsRequired()
            .HasDefaultValueDateTimeNowUtcSql();
        
        builder.Property(x => x.Barcode).IsRequired()
            .HasMaxLength(50);
        builder.Property(x => x.Barcode).IsRequired()
            .HasMaxLength(100);
    }
}