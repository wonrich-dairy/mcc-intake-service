using MccIntakeService.Domain.QualityTests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MccIntakeService.Infrastructure.Persistence.Configurations;

public sealed class AlcoholStageRecordConfiguration : IEntityTypeConfiguration<AlcoholStageRecord>
{
    public void Configure(EntityTypeBuilder<AlcoholStageRecord> builder)
    {
        builder.ToTable("quality_test_alcohol_stages");

        builder.HasKey(record => record.Id);

        // Position in the cascade, so the sequence the officer ran survives storage.
        builder.Property(record => record.Order).IsRequired();

        builder.Property(record => record.Stage).HasMaxLength(30).IsRequired();
        builder.Property(record => record.Outcome).HasMaxLength(20).IsRequired();

        builder.HasIndex(record => new { record.QualityTestId, record.Order })
            .IsUnique()
            .HasDatabaseName("ux_quality_test_alcohol_stages_order");
    }
}
