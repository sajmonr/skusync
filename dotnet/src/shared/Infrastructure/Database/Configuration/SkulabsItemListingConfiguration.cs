using Infrastructure.Database.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Database.Configuration;

public class SkulabsItemListingConfiguration : IEntityTypeConfiguration<SkulabsItemListingEntity>
{
    public void Configure(EntityTypeBuilder<SkulabsItemListingEntity> builder)
    {
        builder.ToTable("SkulabsItemListings");

        builder.HasUuidV7PrimaryKey(x => x.SkulabsItemListingId);

        builder.Property(x => x.SkulabsSourceListingId).IsRequired().HasMaxLength(50);
        builder.Property(x => x.RawVariantId).IsRequired().HasMaxLength(100);
        builder.Property(x => x.ShopifyProductId).IsRequired().HasMaxLength(100);

        // One row per SkuLabs listing id, whichever item it hangs off.
        builder.HasIndex(x => x.SkulabsSourceListingId).IsUnique();

        builder.HasIndex(x => x.SkulabsItemId);

        // Deliberately not unique. Two SkuLabs items claiming the same variant is a state SkuLabs
        // permits and we must be able to store in order to show it; SkulabsItemLinks.IsSyncable is
        // what stops it reaching the sync.
        builder.HasIndex(x => x.ShopifyProductVariantId);

        builder.HasOne(x => x.ShopifyProductVariant)
            .WithMany(variant => variant.SkulabsItemListings)
            .HasForeignKey(x => x.ShopifyProductVariantId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
