using MccIntakeService.Domain.Tanks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MccIntakeService.Infrastructure.Persistence.Configurations;

public sealed class ChillingTankConfiguration : IEntityTypeConfiguration<ChillingTank>
{
    public void Configure(EntityTypeBuilder<ChillingTank> builder)
    {
        builder.ToTable("chilling_tanks");

        builder.HasKey(tank => tank.Id);

        builder.Property(tank => tank.Code)
            .HasMaxLength(ChillingTank.MaxCodeLength)
            .IsRequired();

        builder.Property(tank => tank.Name)
            .HasMaxLength(ChillingTank.MaxNameLength)
            .IsRequired();

        builder.Property(tank => tank.CapacityLitres)
            .HasPrecision(10, 2)
            .IsRequired();

        // Tanks that predate dispatch notes are on their first fill.
        builder.Property(tank => tank.FillNumber)
            .HasDefaultValue(1)
            .IsRequired();

        builder.Property(tank => tank.LastClosedAtUtc);

        builder.HasIndex(tank => tank.Code)
            .IsUnique()
            .HasDatabaseName("ux_chilling_tanks_code");

        builder.HasData(ChillingTankSeed.Tanks);
    }
}

public sealed class TankPourConfiguration : IEntityTypeConfiguration<TankPour>
{
    public void Configure(EntityTypeBuilder<TankPour> builder)
    {
        builder.ToTable("tank_pours");

        builder.HasKey(pour => pour.Id);

        builder.Property(pour => pour.QuantityLitres).HasPrecision(10, 2).IsRequired();
        builder.Property(pour => pour.QuantityKg).HasPrecision(10, 2).IsRequired();
        builder.Property(pour => pour.FillNumber).HasDefaultValue(1).IsRequired();
        builder.Property(pour => pour.PouredBy).HasMaxLength(100);
        builder.Property(pour => pour.PouredAtUtc).IsRequired();
        builder.Property(pour => pour.PourDate).IsRequired();

        // A consignment goes into exactly one tank, and never twice.
        builder.HasIndex(pour => pour.ConsignmentId)
            .IsUnique()
            .HasDatabaseName("ux_tank_pours_consignment");

        // The manifest is queried by tank and by date, so index the pair.
        builder.HasIndex(pour => new { pour.TankId, pour.PourDate })
            .HasDatabaseName("ix_tank_pours_tank_date");

        // What a tank holds, and what a dispatch note resolves through, are both read by fill.
        builder.HasIndex(pour => new { pour.TankId, pour.FillNumber })
            .HasDatabaseName("ix_tank_pours_tank_fill");

        builder.HasOne(pour => pour.Tank)
            .WithMany()
            .HasForeignKey(pour => pour.TankId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(pour => pour.Consignment)
            .WithOne()
            .HasForeignKey<TankPour>(pour => pour.ConsignmentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
