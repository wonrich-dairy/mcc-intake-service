using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Wonrich.Auth.Authorization;
using Wonrich.AuthService.Domain;
using Wonrich.AuthService.Infrastructure;

namespace Wonrich.AuthService.Tests.Support;

/// <summary>Hosts the auth service over SQLite so its HTTP contract can be driven directly.</summary>
internal sealed class AuthApiFactory : WebApplicationFactory<Program>
{
    private const string PlaceholderConnectionString =
        "Server=localhost;Port=3307;Database=wonrich_auth_tests;User=tests;Password=tests";

    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Development);
        builder.UseSetting("ConnectionStrings:DefaultConnection", PlaceholderConnectionString);
        builder.UseSetting("Auth:SigningKey", AuthTestHost.SigningKey);
        builder.UseSetting("Auth:Issuer", "wonrich-auth-tests");
        builder.UseSetting("Auth:Audience", "wonrich-services-tests");

        builder.ConfigureServices(services =>
        {
            RemoveDbContextRegistration(services);

            _connection.Open();

            services.AddDbContext<AuthDbContext>(options => options.UseSqlite(_connection));

            using var scope = services.BuildServiceProvider().CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            context.Database.EnsureCreated();

            context.Users.Add(new UserAccount(
                Guid.NewGuid(),
                "k.perera",
                "Kamal Perera",
                AuthTestHost.KnownPassword,
                WonrichRoles.MccManager,
                "MCC-KANDY"));

            context.SaveChanges();
        });
    }

    /// <summary>Strips the MySQL registration so SQLite is the only provider in play.</summary>
    private static void RemoveDbContextRegistration(IServiceCollection services)
    {
        var registrations = services
            .Where(descriptor =>
                descriptor.ServiceType == typeof(DbContextOptions<AuthDbContext>)
                || descriptor.ServiceType == typeof(DbContextOptions)
                || descriptor.ServiceType == typeof(AuthDbContext)
                || (descriptor.ServiceType.IsGenericType
                    && descriptor.ServiceType.GetGenericTypeDefinition().Name.StartsWith(
                        "IDbContextOptionsConfiguration", StringComparison.Ordinal)))
            .ToList();

        foreach (var registration in registrations)
        {
            services.Remove(registration);
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            _connection.Dispose();
        }
    }
}
