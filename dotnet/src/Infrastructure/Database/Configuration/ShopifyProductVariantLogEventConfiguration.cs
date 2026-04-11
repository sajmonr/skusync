using Infrastructure.Database.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Database.Configuration;

public class ShopifyProductVariantLogEventConfiguration : IEntityTypeConfiguration<ShopifyProductVariantLogEventEntity>
{
    public void Configure(EntityTypeBuilder<ShopifyProductVariantLogEventEntity> builder)
    {
        builder.ToTable("ShopifyProductVariantLogEvents");

        builder.HasUuidV7PrimaryKey(x => x.ShopifyProductVariantLogEventId);

        builder.Property(x => x.CreatedOn)
            .IsRequired()
            .HasDefaultValueDateTimeNowUtcSql();
        
        builder.Property(x => x.Message)
            .IsRequired()
            .HasMaxLength(2000);
        
        builder.HasOne(x => x.ShopifyProductVariant)
            .WithMany(x => x.LogEvents)
            .HasForeignKey(x => x.ShopifyProductVariantId);
    }
}