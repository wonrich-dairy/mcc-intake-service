using MccIntakeService.Domain.Factory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MccIntakeService.Infrastructure.Persistence.Configurations;

public sealed class ArrivalScreeningConfiguration : IEntityTypeConfiguration<ArrivalScreening>
{
    public void Configure(EntityTypeBuilder<ArrivalScreening> builder)
    {
        builder.ToTable("arrival_screenings");

        builder.HasKey(screening => screening.Id);

        builder.Property(screening => screening.ArrivedAtLocal).IsRequired();
        builder.Property(screening => screening.ArrivalDate).IsRequired();
        builder.Property(screening => screening.SmellPassed).IsRequired();
        builder.Property(screening => screening.ColourPassed).IsRequired();
        builder.Property(screening => screening.TemperaturePassed).IsRequired();
        builder.Property(screening => screening.TemperatureCelsius).HasPrecision(5, 2).IsRequired();

        builder.Property(screening => screening.Outcome)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(screening => screening.FailedParameters).HasMaxLength(100);
        builder.Property(screening => screening.ScreenedBy).HasMaxLength(100);
        builder.Property(screening => screening.ScreenedAtUtc).IsRequired();

        // A dispatch note is screened once, pass or fail.
        builder.HasIndex(screening => screening.DispatchNoteId)
            .IsUnique()
            .HasDatabaseName("ux_arrival_screenings_dispatch_note");

        builder.HasOne(screening => screening.DispatchNote)
            .WithOne()
            .HasForeignKey<ArrivalScreening>(screening => screening.DispatchNoteId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(screening => screening.Batch)
            .WithOne()
            .HasForeignKey<Batch>(batch => batch.ArrivalScreeningId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(screening => screening.Batch)
            .UsePropertyAccessMode(PropertyAccessMode.Property);
    }
}

public sealed class BatchConfiguration : IEntityTypeConfiguration<Batch>
{
    public void Configure(EntityTypeBuilder<Batch> builder)
    {
        builder.ToTable("batches");

        builder.HasKey(batch => batch.Id);

        builder.Property(batch => batch.Reference).HasMaxLength(30).IsRequired();
        builder.Property(batch => batch.BatchDate).IsRequired();
        builder.Property(batch => batch.CreatedAtUtc).IsRequired();

        builder.HasIndex(batch => batch.Reference)
            .IsUnique()
            .HasDatabaseName("ux_batches_reference");

        // One dispatch note cannot produce more than one batch.
        builder.HasIndex(batch => batch.DispatchNoteId)
            .IsUnique()
            .HasDatabaseName("ux_batches_dispatch_note");

        builder.HasIndex(batch => batch.BatchDate)
            .HasDatabaseName("ix_batches_date");

        builder.HasOne(batch => batch.DispatchNote)
            .WithMany()
            .HasForeignKey(batch => batch.DispatchNoteId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
