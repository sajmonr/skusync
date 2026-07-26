using Infrastructure.Database.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Database.Configuration;

public class SkulabsAmbiguousItemListingConfiguration
    : IEntityTypeConfiguration<SkulabsAmbiguousItemListingEntity>
{
    public void Configure(EntityTypeBuilder<SkulabsAmbiguousItemListingEntity> builder)
    {
        builder.ToTable("SkulabsAmbiguousItemListings");

        builder.HasUuidV7PrimaryKey(x => x.SkulabsAmbiguousItemListingId);

        builder.Property(x => x.SkulabsSourceListingId).IsRequired().HasMaxLength(50);
        builder.Property(x => x.RawVariantId).IsRequired().HasMaxLength(100);
        builder.Property(x => x.ShopifyProductId).IsRequired().HasMaxLength(100);

        builder.HasIndex(x => x.SkulabsAmbiguousItemId);

        // Informational link only — no uniqueness. Several ambiguous listings may reference the same
        // variant, and a variant with a valid active link may also be referenced ambiguously here.
        builder.HasOne(x => x.ShopifyProductVariant)
            .WithMany()
            .HasForeignKey(x => x.ShopifyProductVariantId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
