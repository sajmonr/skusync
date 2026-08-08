using Infrastructure.Database.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Database.Configuration;

public class DesiredItemStateConfiguration : IEntityTypeConfiguration<DesiredItemStateEntity>
{
    public void Configure(EntityTypeBuilder<DesiredItemStateEntity> builder)
    {
        builder.ToTable("DesiredItemStates");

        builder.HasUuidV7PrimaryKey(x => x.DesiredItemStateId);

        // Column widths track the mirrors they are compared against; a desired value that could not
        // fit its mirror would read as permanently dirty and be pushed on every cycle forever.
        builder.Property(x => x.Sku).IsRequired().HasMaxLength(100);
        builder.Property(x => x.Barcode).IsRequired().HasMaxLength(100);
        builder.Property(x => x.Title).IsRequired().HasMaxLength(1000);
        builder.Property(x => x.Location).IsRequired().HasMaxLength(100).HasDefaultValue("");

        builder.Property(x => x.CreatedOnUtc).IsRequired().HasDefaultValueDateTimeNowUtcSql();
        builder.Property(x => x.UpdatedOnUtc).IsRequired().HasDefaultValueDateTimeNowUtcSql();

        // One desired state per variant, enforced rather than assumed: two rows would make "what
        // should this variant hold" ambiguous and let the two take turns pushing opposite values.
        builder.HasIndex(x => x.ShopifyProductVariantId).IsUnique();

        builder.HasOne(x => x.ShopifyProductVariant)
            .WithOne(variant => variant.DesiredState)
            .HasForeignKey<DesiredItemStateEntity>(x => x.ShopifyProductVariantId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
