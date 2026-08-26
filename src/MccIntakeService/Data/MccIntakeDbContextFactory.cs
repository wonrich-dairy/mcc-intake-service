using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MccIntakeService.Data;

/// <summary>
/// Design-time factory for EF Core migrations.
/// Used by 'dotnet ef migrations add' when no running host is available.
/// </summary>
public class MccIntakeDbContextFactory : IDesignTimeDbContextFactory<MccIntakeDbContext>
{
    public MccIntakeDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<MccIntakeDbContext>();

        // Design-time connection — only used for generating migrations, not at runtime.
        // At runtime, Program.cs reads the connection string from configuration.
        optionsBuilder.UseMySQL(
            "Server=localhost;Port=3307;Database=mcc_intake;User=mcc_user;Password=DevPassword123!");

        return new MccIntakeDbContext(optionsBuilder.Options);
    }
}
