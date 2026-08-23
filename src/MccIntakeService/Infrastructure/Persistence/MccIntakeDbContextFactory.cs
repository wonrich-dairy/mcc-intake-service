using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MccIntakeService.Infrastructure.Persistence;

/// <summary>
/// Used by <c>dotnet ef</c> to build a context outside the running host, so migrations can be
/// scaffolded without a MySQL server being reachable. The connection string is only a placeholder
/// unless one is supplied through the ConnectionStrings__DefaultConnection environment variable.
/// </summary>
public sealed class MccIntakeDbContextFactory : IDesignTimeDbContextFactory<MccIntakeDbContext>
{
    public MccIntakeDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? DatabaseDefaults.DesignTimeConnectionString;

        var serverVersion = ServerVersion.Parse(
            Environment.GetEnvironmentVariable("Database__ServerVersion") ?? DatabaseDefaults.DefaultServerVersion);

        var options = new DbContextOptionsBuilder<MccIntakeDbContext>()
            .UseMySql(connectionString, serverVersion)
            .Options;

        return new MccIntakeDbContext(options);
    }
}
