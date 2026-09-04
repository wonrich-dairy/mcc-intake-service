using MccIntakeService.Domain.Consignments;
using MccIntakeService.Domain.Dispatch;
using MccIntakeService.Domain.Factory;
using MccIntakeService.Domain.QualityTests;
using MccIntakeService.Domain.Societies;
using MccIntakeService.Domain.Sync;
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

    /// <summary>Temperature readings taken against the chilling tanks (SCRUM-52).</summary>
    public DbSet<TankTemperatureReading> TankTemperatureReadings => Set<TankTemperatureReading>();

    /// <summary>Bowser dispatch notes (SCRUM-8).</summary>
    public DbSet<DispatchNote> DispatchNotes => Set<DispatchNote>();

    /// <summary>The per-tank quantities each dispatch note drew.</summary>
    public DbSet<DispatchSource> DispatchSources => Set<DispatchSource>();

    /// <summary>Factory arrival screenings (SCRUM-9), recorded whether accepted or rejected.</summary>
    public DbSet<ArrivalScreening> ArrivalScreenings => Set<ArrivalScreening>();

    /// <summary>Production batches created by a passing arrival screening (SCRUM-9).</summary>
    public DbSet<Batch> Batches => Set<Batch>();

    /// <summary>Offline records already uploaded, so a replayed queue applies once (SCRUM-10).</summary>
    public DbSet<SyncedRecord> SyncedRecords => Set<SyncedRecord>();

    /// <summary>
    /// Chilling centres registered in the system (SCRUM-36). Distinct from <see cref="Society"/>:
    /// a centre is where milk is received, a society is who supplies it.
    /// </summary>
    public DbSet<MilkCollectionCenter> MilkCollectionCenters => Set<MilkCollectionCenter>();

    /// <summary>
    /// Every <see cref="DateOnly"/> in the model is stored through
    /// <see cref="DateOnlyToDateTimeConverter"/>, because the MySQL provider cannot read one back.
    /// Applying it as a convention rather than per property means a date added to a later entity is
    /// covered without anyone having to remember this.
    /// </summary>
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);

        var dates = configurationBuilder
            .Properties<DateOnly>()
            .HaveConversion<DateOnlyToDateTimeConverter>();

        // Naming the store type keeps the columns `date` rather than the `datetime(6)` the converted
        // CLR type would otherwise infer, so the mapping matches the schema already migrated. SQLite,
        // which the tests run on, has no `date` type and maps the converted value itself.
        if (Database.ProviderName?.Contains("MySql", StringComparison.OrdinalIgnoreCase) == true)
        {
            dates.HaveColumnType("date");
        }
    }

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
