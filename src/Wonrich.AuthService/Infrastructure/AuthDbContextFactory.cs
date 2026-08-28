using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Wonrich.AuthService.Infrastructure;

/// <summary>
/// Builds a context for the EF tooling, so migrations can be scaffolded without a running MySQL
/// server and without the real connection string being present.
/// </summary>
public sealed class AuthDbContextFactory : IDesignTimeDbContextFactory<AuthDbContext>
{
    private const string DesignTimeConnectionString =
        "Server=localhost;Port=3307;Database=wonrich_auth;User=auth_user;Password=DesignTimeOnly";

    public AuthDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AuthDbContext>()
            .UseMySql(DesignTimeConnectionString, ServerVersion.Parse("8.0.36-mysql"))
            .Options;

        return new AuthDbContext(options);
    }
}
