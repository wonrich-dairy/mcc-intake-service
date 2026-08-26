using Microsoft.EntityFrameworkCore;
using MccIntakeService.Models;

namespace MccIntakeService.Data;

/// <summary>
/// Entity Framework database context for MCC &amp; Intake Service.
/// </summary>
public class MccIntakeDbContext : DbContext
{
    public MccIntakeDbContext(DbContextOptions<MccIntakeDbContext> options)
        : base(options)
    {
    }

    /// <summary>
    /// Milk Collection Centers registered in the system.
    /// </summary>
    public DbSet<MilkCollectionCenter> MilkCollectionCenters => Set<MilkCollectionCenter>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<MilkCollectionCenter>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Location).HasMaxLength(500);
            entity.Property(e => e.ContactNumber).HasMaxLength(20);
            entity.HasIndex(e => e.Code).IsUnique();
        });
    }
}
