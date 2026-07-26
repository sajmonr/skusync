using Infrastructure.Database.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SharedKernel;

namespace Infrastructure.Database.Configuration;

public class SkulabsAmbiguityReasonConfiguration : IEntityTypeConfiguration<SkulabsAmbiguityReasonEntity>
{
    public void Configure(EntityTypeBuilder<SkulabsAmbiguityReasonEntity> builder)
    {
        builder.ToTable("SkulabsAmbiguityReasons");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Name).IsRequired().HasMaxLength(50);

        builder.HasData(Enum.GetValues<SkulabsAmbiguityReason>()
            .Select(reason => new SkulabsAmbiguityReasonEntity
            {
                Id = reason,
                Name = reason.ToString()
            }));
    }
}
