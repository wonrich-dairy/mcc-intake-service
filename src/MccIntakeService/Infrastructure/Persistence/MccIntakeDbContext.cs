using MccIntakeService.Domain.Consignments;
using MccIntakeService.Domain.Dispatch;
using MccIntakeService.Domain.QualityTests;
using MccIntakeService.Domain.Societies;
using MccIntakeService.Domain.Tanks;
using MccIntakeService.Models;
using Microsoft.EntityFrameworkCore;

namespace MccIntakeService.Infrastructure.Persistence;

/// <summary>Entity Framework context for the MCC &amp; Intake Service datastore (SCRUM-36).</summary>
public class MccIntakeDbContext : DbContext
{
    public MccIntakeDbContext(DbContextOptions<MccIntakeDbContext> options) : base(options)
    {
    }

    public DbSet<Society> Societies => Set<Society>();

    public DbSet<Consignment> Consignments => Set<Consignment>();

    public DbSet<ConsignmentCan> ConsignmentCans => Set<ConsignmentCan>();

    /// <summary>Gate quality test panels (SCRUM-7); one per consignment.</summary>
    public DbSet<QualityTest> QualityTests => Set<QualityTest>();

    /// <summary>The centre's chilling tanks (SCRUM-52).</summary>
    public DbSet<ChillingTank> ChillingTanks => Set<ChillingTank>();

    /// <summary>Accepted consignments poured into tanks (SCRUM-52).</summary>
    public DbSet<TankPour> TankPours => Set<TankPour>();

    /// <summary>Bowser dispatch notes (SCRUM-8).</summary>
    public DbSet<DispatchNote> DispatchNotes => Set<DispatchNote>();

    /// <summary>The per-tank quantities each dispatch note drew.</summary>
    public DbSet<DispatchSource> DispatchSources => Set<DispatchSource>();

    /// <summary>
    /// Chilling centres registered in the system (SCRUM-36). Distinct from <see cref="Society"/>:
    /// a centre is where milk is received, a society is who supplies it.
    /// </summary>
    public DbSet<MilkCollectionCenter> MilkCollectionCenters => Set<MilkCollectionCenter>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MccIntakeDbContext).Assembly);

        // Brought over from the SCRUM-36 context this one replaced.
        modelBuilder.Entity<MilkCollectionCenter>(entity =>
        {
            entity.HasKey(centre => centre.Id);
            entity.Property(centre => centre.Name).IsRequired().HasMaxLength(200);
            entity.Property(centre => centre.Location).HasMaxLength(500);
            entity.Property(centre => centre.ContactNumber).HasMaxLength(20);
            entity.HasIndex(centre => centre.Code).IsUnique();
        });
    }
}
