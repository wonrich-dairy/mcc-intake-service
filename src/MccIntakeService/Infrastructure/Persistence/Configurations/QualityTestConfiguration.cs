using MccIntakeService.Domain.QualityTests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MccIntakeService.Infrastructure.Persistence.Configurations;

public sealed class QualityTestConfiguration : IEntityTypeConfiguration<QualityTest>
{
    public void Configure(EntityTypeBuilder<QualityTest> builder)
    {
        builder.ToTable("quality_tests");

        builder.HasKey(test => test.Id);

        builder.Property(test => test.FatPercent).HasPrecision(5, 2).IsRequired();
        builder.Property(test => test.RawLactometerReading).HasPrecision(5, 2).IsRequired();
        builder.Property(test => test.TemperatureCelsius).HasPrecision(5, 2).IsRequired();
        builder.Property(test => test.WaterPercent).HasPrecision(5, 2).IsRequired();
        builder.Property(test => test.CorrectedClr).HasPrecision(5, 2).IsRequired();
        builder.Property(test => test.Snf).HasPrecision(5, 2).IsRequired();
        builder.Property(test => test.TotalSolids).HasPrecision(5, 2).IsRequired();

        // Stored by name rather than ordinal: the KQ card and the grade set can gain entries, and
        // a stored ordinal would silently reinterpret every panel already recorded.
        builder.Property(test => test.KqColour).HasMaxLength(30).IsRequired();
        builder.Property(test => test.StabilityGrade).HasMaxLength(30).IsRequired();
        builder.Property(test => test.PassedAlcoholAt).HasMaxLength(30).IsRequired();

        // Recorded as found, defaulting to sound: a panel written before the sensory check existed
        // did not observe a fault, and reading it back as one would restate history.
        builder.Property(test => test.SmellOk).IsRequired().HasDefaultValue(true);
        builder.Property(test => test.ColourOk).IsRequired().HasDefaultValue(true);
        builder.Property(test => test.TasteOk).IsRequired().HasDefaultValue(true);

        builder.Property(test => test.Verdict)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(test => test.FailedParameter).HasMaxLength(50);
        builder.Property(test => test.FailedValue).HasMaxLength(50);
        builder.Property(test => test.TestedBy).HasMaxLength(100);
        builder.Property(test => test.TestedAtUtc).IsRequired();

        // A consignment is tested exactly once. The unique index is what settles a race between
        // two officers submitting at the same moment; the service check only gives a better error.
        builder.HasIndex(test => test.ConsignmentId)
            .IsUnique()
            .HasDatabaseName("ux_quality_tests_consignment");

        builder.HasOne(test => test.Consignment)
            .WithOne()
            .HasForeignKey<QualityTest>(test => test.ConsignmentId)
            .OnDelete(DeleteBehavior.Cascade);

        // Mapped the same way as Consignment.Cans: the collection is exposed read-only, so EF
        // reads and writes the backing field, and the stages always load with their test.
        builder.HasMany(test => test.AlcoholStages)
            .WithOne()
            .HasForeignKey(record => record.QualityTestId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(test => test.AlcoholStages)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .AutoInclude();
    }
}
