using Microsoft.EntityFrameworkCore;
using Wonrich.Auth.Authorization;
using Wonrich.AuthService.Domain;

namespace Wonrich.AuthService.Infrastructure;

/// <summary>
/// Options for the starter accounts a fresh environment is seeded with (SCRUM-45).
/// </summary>
public sealed class SeedOptions
{
    public const string SectionName = "Seed";

    /// <summary>Whether to create the starter accounts when no user exists.</summary>
    public bool Enabled { get; set; }

    /// <summary>Password every seeded account is created with.</summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>Facility stamped on the seeded accounts.</summary>
    public string Facility { get; set; } = "MCC-KANDY";
}

/// <summary>
/// Creates one account per Wonrich role the first time a database comes up empty.
/// </summary>
/// <remarks>
/// <para>
/// Account administration is the System Administrator's alone, and there is no way to become one:
/// <c>POST /api/users</c> is behind that same policy, so an empty database could not issue its own
/// first administrator. Every fresh environment had to be opened with hand-written SQL, and the
/// password hash had to be reproduced outside the domain that owns it.
/// </para>
/// <para>
/// Seeding runs only when the table is empty, so it never revives a deleted account, never
/// overwrites a changed password, and does nothing on the second start. It is off unless a
/// password is configured, which keeps it from putting known credentials into an environment that
/// did not ask for them.
/// </para>
/// </remarks>
public static class AuthDbSeeder
{
    private static readonly (string UserName, string DisplayName, string Role)[] Accounts =
    [
        ("admin", "System Administrator", WonrichRoles.SystemAdministrator),
        ("manager", "MCC Manager", WonrichRoles.MccManager),
        ("officer", "Intake Officer", WonrichRoles.IntakeOfficer),
        ("analyst", "Quality Analyst", WonrichRoles.QualityAnalyst),
        ("factory", "Factory Intake Officer", WonrichRoles.FactoryIntakeOfficer),
        ("production", "Production Manager", WonrichRoles.ProductionManager),
    ];

    /// <summary>
    /// Adds the starter accounts when the database holds none. Returns the usernames created,
    /// which is empty on every start after the first.
    /// </summary>
    public static async Task<IReadOnlyList<string>> SeedAsync(
        AuthDbContext dbContext,
        SeedOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(options);

        if (!options.Enabled || string.IsNullOrWhiteSpace(options.Password))
        {
            return [];
        }

        if (await dbContext.Users.AnyAsync(cancellationToken))
        {
            return [];
        }

        var created = new List<string>();

        foreach (var (userName, displayName, role) in Accounts)
        {
            dbContext.Users.Add(new UserAccount(
                Guid.NewGuid(),
                userName,
                displayName,
                options.Password,
                role,
                options.Facility));

            created.Add(userName);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return created;
    }
}
