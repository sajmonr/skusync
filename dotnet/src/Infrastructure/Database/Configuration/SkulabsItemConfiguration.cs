using Infrastructure.Database.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Database.Configuration;

public class SkulabsItemConfiguration : IEntityTypeConfiguration<SkulabsItemEntity>
{
    public void Configure(EntityTypeBuilder<SkulabsItemEntity> builder)
    {
        builder.ToTable("SkulabsItems");

        builder.HasUuidV7PrimaryKey(x => x.SkulabsItemId);
        
        builder.Property(x => x.SkulabsSourceId).IsRequired().HasMaxLength(50);
        builder.Property(x => x.Title).IsRequired().HasMaxLength(1000);
        builder.Property(x => x.Sku).IsRequired().HasMaxLength(100);
        builder.Property(x => x.Barcode).IsRequired().HasMaxLength(100);
        
        builder.HasIndex(x => x.SkulabsSourceId).IsUnique();
        
        builder.HasOne(x => x.ShopifyProductVariant)
            .WithOne(x => x.SkulabsItem)
            .HasForeignKey<SkulabsItemEntity>(x => x.SkulabsItemId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}