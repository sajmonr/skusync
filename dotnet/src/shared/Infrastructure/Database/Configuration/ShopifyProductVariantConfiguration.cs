using Infrastructure.Database.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Database.Configuration;

public class ShopifyProductVariantConfiguration : IEntityTypeConfiguration<ShopifyProductVariantEntity>
{
    public void Configure(EntityTypeBuilder<ShopifyProductVariantEntity> builder)
    {
        builder.ToTable("ShopifyProductVariants");

        builder.HasUuidV7PrimaryKey(x => x.ShopifyProductVariantId);

        builder.Property(x => x.GlobalProductId)
            .IsRequired()
            .HasMaxLength(255);
        builder.Property(x => x.GlobalVariantId).IsRequired().HasMaxLength(255);
        
        builder.Property(x => x.DisplayName).IsRequired().HasMaxLength(1000);

        builder.Property(x => x.ProductId).IsRequired();
        builder.Property(x => x.VariantId).IsRequired();

        builder.Property(x => x.CreatedOnUtc)
            .IsRequired()
            .HasDefaultValueDateTimeNowUtcSql();
        
        builder.Property(x => x.UpdatedOnUtc)
            .IsRequired()
            .HasDefaultValueDateTimeNowUtcSql();
        
        builder.Property(x => x.Barcode).IsRequired()
            .HasMaxLength(100);
        builder.Property(x => x.Sku).IsRequired()
            .HasMaxLength(100);

        builder.Property(x => x.PendingShopifySync)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(x => x.FailedShopifySyncAttempts)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(x => x.IsActive)
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(x => x.IsDeleted)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(x => x.DeletedOn)
            .IsRequired()
            .HasDefaultValueDateTimeMinSql();

        builder.HasIndex(x => x.GlobalVariantId).IsUnique();
        builder.HasIndex(x => x.VariantId).IsUnique();
        // Filtered index so the drift sweep scans a small subset even at high variant counts.
        builder.HasIndex(x => x.PendingShopifySync)
            .HasFilter("\"PendingShopifySync\" = true");

        // Filtered index over the rows eligible for sync work. The maintenance sweeps and the
        // item-sync list skip both deactivated (IsActive=false) and deleted (IsDeleted=true)
        // variants, filtering on these columns explicitly; there is deliberately no global query
        // filter, because every key-matching read path (import, webhooks, SkuLabs reconciliation,
        // SKU uniqueness) must see excluded rows or it re-inserts them and violates the unique
        // GlobalVariantId/VariantId index.
        builder.HasIndex(x => x.IsActive)
            .HasFilter("\"IsActive\" = true AND \"IsDeleted\" = false");
    }
}