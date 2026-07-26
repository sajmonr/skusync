using Infrastructure.Database.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SharedKernel;

namespace Infrastructure.Database.Configuration;

public class SkulabsAmbiguityStatusConfiguration : IEntityTypeConfiguration<SkulabsAmbiguityStatusEntity>
{
    public void Configure(EntityTypeBuilder<SkulabsAmbiguityStatusEntity> builder)
    {
        builder.ToTable("SkulabsAmbiguityStatuses");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Name).IsRequired().HasMaxLength(50);

        builder.HasData(Enum.GetValues<SkulabsAmbiguityStatus>()
            .Select(status => new SkulabsAmbiguityStatusEntity
            {
                Id = status,
                Name = status.ToString()
            }));
    }
}
