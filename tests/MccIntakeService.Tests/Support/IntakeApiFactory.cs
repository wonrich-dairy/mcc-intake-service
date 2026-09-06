using MccIntakeService.Application.Abstractions;
using MccIntakeService.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Wonrich.Auth.Authorization;
using Wonrich.Auth.Tokens;

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

    /// <summary>Signing key the hosted service validates with and the tests sign with.</summary>
    private const string TestSigningKey = "wonrich-integration-test-signing-key-0123456789";

    private const string TestIssuer = "wonrich-auth-tests";

    private const string TestAudience = "wonrich-services-tests";

    /// <summary>The one browser origin the hosted service is configured to accept (SCRUM-92).</summary>
    public const string AllowedTestOrigin = "http://localhost:5173";


    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    /// <summary>The clock the hosted application uses; move it to test cutoff behaviour over HTTP.</summary>
    public FakeIntakeClock Clock { get; } = new(new DateTime(2026, 8, 23, 8, 0, 0));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Development);
        builder.UseSetting("ConnectionStrings:DefaultConnection", PlaceholderConnectionString);
        builder.UseSetting("Auth:SigningKey", TestSigningKey);
        builder.UseSetting("Auth:Issuer", TestIssuer);
        builder.UseSetting("Auth:Audience", TestAudience);

        builder.UseSetting("Cors:AllowedOrigins:0", AllowedTestOrigin);

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
    /// A client bearing a genuine JWT for the given role, signed with the same key the hosted
    /// service validates against, so the tests exercise the real token path.
    /// </summary>
    public HttpClient CreateClientAs(string role, string facility = "MCC-KANDY")
    {
        var client = CreateClient();

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", IssueTokenFor(role, facility));

        return client;
    }

    /// <summary>A client authorised to maintain societies — the usual caller in these tests.</summary>
    public HttpClient CreateManagerClient() => CreateClientAs(WonrichRoles.MccManager);

    /// <summary>Mints an access token the hosted service will accept.</summary>
    public static string IssueTokenFor(string role, string facility = "MCC-KANDY") =>
        IssueTokenSignedWith(TestSigningKey, role, facility);

    /// <summary>
    /// Mints a token signed with an arbitrary key, so a forged one can be told apart from a
    /// genuine one in the tests.
    /// </summary>
    public static string IssueTokenSignedWith(
        string signingKey,
        string role,
        string facility = "MCC-KANDY")
    {
        var issuer = new AccessTokenIssuer(
            Options.Create(new WonrichJwtOptions
            {
                Issuer = TestIssuer,
                Audience = TestAudience,
                SigningKey = signingKey
            }),
            TimeProvider.System);

        return issuer.Issue(new TokenSubject("test-user-id", "test-user", facility, role)).AccessToken;
    }

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
