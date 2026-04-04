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
        builder.Property(x => x.Title).IsRequired().HasMaxLength(400);

        builder.Property(x => x.ProductId).IsRequired();
        builder.Property(x => x.VariantId).IsRequired();

        builder.Property(x => x.CreatedOnUtc)
            .IsRequired()
            .HasDefaultValueDateTimeNowUtcSql();
        
        builder.Property(x => x.UpdatedOnUtc)
            .IsRequired()
            .HasDefaultValueDateTimeNowUtcSql();
    }
}