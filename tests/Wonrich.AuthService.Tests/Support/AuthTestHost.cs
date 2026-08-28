using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Wonrich.Auth.Authorization;
using Wonrich.Auth.Tokens;
using Wonrich.AuthService.Application;
using Wonrich.AuthService.Domain;
using Wonrich.AuthService.Infrastructure;

namespace Wonrich.AuthService.Tests.Support;

/// <summary>
/// A SQLite-backed auth database and the services around it, so the sign-in and refresh paths can
/// be exercised without a MySQL server.
/// </summary>
internal sealed class AuthTestHost : IDisposable
{
    public const string SigningKey = "wonrich-auth-service-test-signing-key-0123456789";

    public const string KnownPassword = "correct-horse-battery-staple";

    private readonly SqliteConnection _connection;

    private AuthTestHost(SqliteConnection connection)
    {
        _connection = connection;
    }

    /// <summary>Clock the service reads; move it to test expiry.</summary>
    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 8, 28, 8, 0, 0, TimeSpan.Zero));

    public WonrichJwtOptions Options { get; } = new()
    {
        Issuer = "wonrich-auth-tests",
        Audience = "wonrich-services-tests",
        SigningKey = SigningKey,
        AccessTokenMinutes = 60,
        RefreshTokenDays = 7
    };

    public static AuthTestHost Create()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var host = new AuthTestHost(connection);

        using var context = host.CreateContext();
        context.Database.EnsureCreated();

        return host;
    }

    public AuthDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AuthDbContext>().UseSqlite(_connection).Options);

    /// <summary>Adds an account with <see cref="KnownPassword"/> as its password.</summary>
    public UserAccount SeedUser(
        string userName = "k.perera",
        string role = WonrichRoles.MccManager,
        string? facility = "MCC-KANDY",
        bool active = true)
    {
        using var context = CreateContext();

        var user = new UserAccount(Guid.NewGuid(), userName, "Kamal Perera", KnownPassword, role, facility);

        if (!active)
        {
            user.Deactivate();
        }

        context.Users.Add(user);
        context.SaveChanges();

        return user;
    }

    public AuthenticationService CreateService(AuthDbContext context) =>
        new(
            context,
            new AccessTokenIssuer(Microsoft.Extensions.Options.Options.Create(Options), Time),
            Microsoft.Extensions.Options.Options.Create(Options),
            Time,
            NullLogger<AuthenticationService>.Instance);

    public void Dispose() => _connection.Dispose();
}

/// <summary>A clock the tests move by hand.</summary>
internal sealed class FakeTimeProvider : TimeProvider
{
    private DateTimeOffset _utcNow;

    public FakeTimeProvider(DateTimeOffset utcNow)
    {
        _utcNow = utcNow;
    }

    public override DateTimeOffset GetUtcNow() => _utcNow;

    /// <summary>Moves the clock forward.</summary>
    public void Advance(TimeSpan by) => _utcNow = _utcNow.Add(by);
}
