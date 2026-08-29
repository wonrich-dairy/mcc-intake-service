using MccIntakeService.Domain.Dispatch;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MccIntakeService.Infrastructure.Persistence.Configurations;

public sealed class DispatchNoteConfiguration : IEntityTypeConfiguration<DispatchNote>
{
    public void Configure(EntityTypeBuilder<DispatchNote> builder)
    {
        builder.ToTable("dispatch_notes");

        builder.HasKey(note => note.Id);

        builder.Property(note => note.Reference).HasMaxLength(30).IsRequired();
        builder.Property(note => note.BowserRegistration)
            .HasMaxLength(DispatchNote.MaxBowserRegistrationLength).IsRequired();
        builder.Property(note => note.DriverName)
            .HasMaxLength(DispatchNote.MaxDriverNameLength).IsRequired();
        builder.Property(note => note.DispatchedAtLocal).IsRequired();
        builder.Property(note => note.DispatchDate).IsRequired();

        builder.Property(note => note.TotalQuantityLitres).HasPrecision(10, 2).IsRequired();
        builder.Property(note => note.FatPercent).HasPrecision(5, 2).IsRequired();
        builder.Property(note => note.Snf).HasPrecision(5, 2).IsRequired();
        builder.Property(note => note.TemperatureCelsius).HasPrecision(5, 2).IsRequired();

        // Stored by name, like the gate panel: both scales can gain entries, and a stored ordinal
        // would silently reinterpret notes already issued.
        builder.Property(note => note.KqColour).HasMaxLength(30).IsRequired();
        builder.Property(note => note.StabilityGrade).HasMaxLength(30).IsRequired();

        builder.Property(note => note.Remarks).HasMaxLength(DispatchNote.MaxRemarksLength);
        builder.Property(note => note.DispatchedBy).HasMaxLength(100);
        builder.Property(note => note.RecordedAtUtc).IsRequired();

        builder.HasIndex(note => note.Reference)
            .IsUnique()
            .HasDatabaseName("ux_dispatch_notes_reference");

        builder.HasIndex(note => note.DispatchDate)
            .HasDatabaseName("ix_dispatch_notes_date");

        builder.HasMany(note => note.Sources)
            .WithOne()
            .HasForeignKey(source => source.DispatchNoteId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(note => note.Sources)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .AutoInclude();
    }
}

public sealed class DispatchSourceConfiguration : IEntityTypeConfiguration<DispatchSource>
{
    public void Configure(EntityTypeBuilder<DispatchSource> builder)
    {
        builder.ToTable("dispatch_sources");

        builder.HasKey(source => source.Id);

        builder.Property(source => source.QuantityLitres).HasPrecision(10, 2).IsRequired();

        // A tank appears at most once on a note; the per-tank quantities are what the total sums.
        builder.HasIndex(source => new { source.DispatchNoteId, source.TankId })
            .IsUnique()
            .HasDatabaseName("ux_dispatch_sources_note_tank");

        builder.HasOne(source => source.Tank)
            .WithMany()
            .HasForeignKey(source => source.TankId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
