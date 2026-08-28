using MccIntakeService.Api.Infrastructure;
using MccIntakeService.Application.Abstractions;
using MccIntakeService.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MccIntakeService.Tests.Support;

/// <summary>
/// Hosts the real application pipeline — routing, model validation, the domain exception handler
/// and JSON serialisation — over a SQLite database, so the HTTP contract can be exercised without
/// a MySQL server.
/// </summary>
internal sealed class IntakeApiFactory : WebApplicationFactory<Program>
{
    /// <summary>
    /// The host requires a connection string to start, so supply one it can parse. Nothing ever
    /// connects with it: the MySQL registration is replaced with SQLite below.
    /// </summary>
    private const string PlaceholderConnectionString =
        "Server=localhost;Port=3307;Database=mcc_intake_tests;User=tests;Password=tests";

    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    /// <summary>The clock the hosted application uses; move it to test cutoff behaviour over HTTP.</summary>
    public FakeIntakeClock Clock { get; } = new(new DateTime(2026, 8, 23, 8, 0, 0));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Development);
        builder.UseSetting("ConnectionStrings:DefaultConnection", PlaceholderConnectionString);

        builder.ConfigureServices(services =>
        {
            RemoveDbContextRegistration(services);

            _connection.Open();

            services.AddDbContext<MccIntakeDbContext>(options => options.UseSqlite(_connection));
            services.AddSingleton<IIntakeClock>(Clock);

            using var scope = services.BuildServiceProvider().CreateScope();
            scope.ServiceProvider.GetRequiredService<MccIntakeDbContext>().Database.EnsureCreated();
        });
    }

    /// <summary>
    /// Strips the MySQL context registration the host set up, so the SQLite one that follows is
    /// the only provider in play rather than competing with it.
    /// </summary>
    private static void RemoveDbContextRegistration(IServiceCollection services)
    {
        var registrations = services
            .Where(descriptor =>
                descriptor.ServiceType == typeof(DbContextOptions<MccIntakeDbContext>)
                || descriptor.ServiceType == typeof(DbContextOptions)
                || descriptor.ServiceType == typeof(MccIntakeDbContext)
                || (descriptor.ServiceType.IsGenericType
                    && descriptor.ServiceType.GetGenericTypeDefinition().Name.StartsWith(
                        "IDbContextOptionsConfiguration", StringComparison.Ordinal)))
            .ToList();

        foreach (var registration in registrations)
        {
            services.Remove(registration);
        }
    }

    /// <summary>
    /// A client that presents the given roles on every request, for the endpoints SCRUM-51
    /// restricts to MCC Managers and System Administrators.
    /// </summary>
    public HttpClient CreateClientAs(params string[] roles)
    {
        var client = CreateClient();

        client.DefaultRequestHeaders.Add(IntakeAuthentication.RoleHeader, string.Join(',', roles));
        client.DefaultRequestHeaders.Add(IntakeAuthentication.UserHeader, "test-user");

        return client;
    }

    /// <summary>A client authorised to maintain societies — the usual caller in these tests.</summary>
    public HttpClient CreateManagerClient() => CreateClientAs(IntakeRoles.MccManager);

    /// <summary>Runs an assertion against the database the hosted application is using.</summary>
    public async Task WithDbContextAsync(Func<MccIntakeDbContext, Task> action)
    {
        using var scope = Services.CreateScope();
        await action(scope.ServiceProvider.GetRequiredService<MccIntakeDbContext>());
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
