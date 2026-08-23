using MccIntakeService.Domain.Consignments;
using MccIntakeService.Domain.Societies;
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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MccIntakeDbContext).Assembly);
    }
}
