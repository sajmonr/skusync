using Infrastructure.Database.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Database.Configuration;

public class SkulabsAmbiguousItemConfiguration : IEntityTypeConfiguration<SkulabsAmbiguousItemEntity>
{
    public void Configure(EntityTypeBuilder<SkulabsAmbiguousItemEntity> builder)
    {
        builder.ToTable("SkulabsAmbiguousItems");

        builder.HasUuidV7PrimaryKey(x => x.SkulabsAmbiguousItemId);

        builder.Property(x => x.SkulabsSourceItemId).IsRequired().HasMaxLength(50);
        builder.Property(x => x.Name).IsRequired().HasMaxLength(1000);
        builder.Property(x => x.Sku).IsRequired().HasMaxLength(100);
        builder.Property(x => x.Upc).IsRequired().HasMaxLength(100);
        builder.Property(x => x.ListingCount).IsRequired();

        builder.Property(x => x.FirstSeenUtc).HasDefaultValueDateTimeNowUtcSql();
        builder.Property(x => x.LastSeenUtc).HasDefaultValueDateTimeNowUtcSql();

        // One quarantine row per SkuLabs source item — the upsert key for the reconciler.
        builder.HasIndex(x => x.SkulabsSourceItemId).IsUnique();

        builder.HasMany(x => x.Listings)
            .WithOne(listing => listing.AmbiguousItem)
            .HasForeignKey(listing => listing.SkulabsAmbiguousItemId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
