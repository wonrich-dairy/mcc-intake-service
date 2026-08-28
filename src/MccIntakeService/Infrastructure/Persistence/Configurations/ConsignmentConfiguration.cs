using MccIntakeService.Domain.Consignments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MccIntakeService.Infrastructure.Persistence.Configurations;

public sealed class ConsignmentConfiguration : IEntityTypeConfiguration<Consignment>
{
    public void Configure(EntityTypeBuilder<Consignment> builder)
    {
        builder.ToTable("consignments");

        builder.HasKey(consignment => consignment.Id);

        builder.Property(consignment => consignment.Reference)
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(consignment => consignment.ArrivalAtLocal)
            .IsRequired();

        builder.Property(consignment => consignment.ArrivalDate)
            .IsRequired();

        builder.Property(consignment => consignment.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(consignment => consignment.TotalQuantityKg)
            .HasPrecision(10, 2)
            .IsRequired();

        builder.Property(consignment => consignment.TotalQuantityLitres)
            .HasPrecision(10, 2)
            .IsRequired();

        builder.Property(consignment => consignment.RegisteredAtUtc)
            .IsRequired();

        builder.Property(consignment => consignment.RegisteredBy)
            .HasMaxLength(100);

        // The reference is the officer-facing identity of a consignment; the unique index is what
        // actually guarantees no two concurrent registrations claim the same daily sequence number.
        builder.HasIndex(consignment => consignment.Reference)
            .IsUnique()
            .HasDatabaseName("ux_consignments_reference");

        // Supports the "queryable by society and by date" acceptance criteria, and the sequence
        // lookup performed when allocating the next reference for a society on a given day.
        builder.HasIndex(consignment => new { consignment.SocietyId, consignment.ArrivalDate })
            .HasDatabaseName("ix_consignments_society_arrival_date");

        builder.HasOne(consignment => consignment.Society)
            .WithMany()
            .HasForeignKey(consignment => consignment.SocietyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(consignment => consignment.Cans)
            .WithOne()
            .HasForeignKey(can => can.ConsignmentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(consignment => consignment.Cans)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .AutoInclude();
    }
}
