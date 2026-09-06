using Microsoft.EntityFrameworkCore;
using Wonrich.Auth.Authorization;
using Wonrich.AuthService.Domain;
using Wonrich.AuthService.Infrastructure;
using Wonrich.AuthService.Tests.Support;

namespace Wonrich.AuthService.Tests;

/// <summary>
/// Covers the starter accounts a fresh environment comes up with (SCRUM-45). Account
/// administration is behind the System Administrator policy, so without these an empty database
/// cannot issue its own first administrator.
/// </summary>
public sealed class AuthDbSeederTests : IDisposable
{
    private readonly AuthTestHost _host = AuthTestHost.Create();

    public void Dispose() => _host.Dispose();

    private static SeedOptions Enabled() =>
        new() { Enabled = true, Password = "SeedPassw0rd!", Facility = "MCC-KANDY" };

    [Fact]
    public async Task An_empty_database_is_given_one_account_for_every_role()
    {
        await using var context = _host.CreateContext();

        var created = await AuthDbSeeder.SeedAsync(context, Enabled());

        Assert.Equal(WonrichRoles.All.Count, created.Count);

        var roles = await context.Users.Select(user => user.Role).ToListAsync();
        Assert.Equal(WonrichRoles.All.OrderBy(role => role), roles.OrderBy(role => role));
    }

    [Fact]
    public async Task The_seeded_administrator_can_sign_in_with_the_configured_password()
    {
        await using var context = _host.CreateContext();

        await AuthDbSeeder.SeedAsync(context, Enabled());

        var admin = await context.Users.SingleAsync(user => user.UserName == "admin");

        Assert.Equal(WonrichRoles.SystemAdministrator, admin.Role);
        Assert.True(new PasswordHash(admin.PasswordHash).Matches("SeedPassw0rd!"));
        Assert.False(new PasswordHash(admin.PasswordHash).Matches("something else"));
    }

    [Fact]
    public async Task Seeding_a_database_that_already_has_users_changes_nothing()
    {
        await using var context = _host.CreateContext();

        await AuthDbSeeder.SeedAsync(context, Enabled());

        var admin = await context.Users.SingleAsync(user => user.UserName == "admin");
        admin.ChangePassword("SomethingTheAdminChoseLater!");
        await context.SaveChangesAsync();

        var second = await AuthDbSeeder.SeedAsync(context, Enabled());

        Assert.Empty(second);
        Assert.Equal(WonrichRoles.All.Count, await context.Users.CountAsync());

        // The point of running only on an empty table: a restart must not put the known password
        // back on an account whose password has since been changed.
        var unchanged = await context.Users.SingleAsync(user => user.UserName == "admin");
        Assert.True(new PasswordHash(unchanged.PasswordHash).Matches("SomethingTheAdminChoseLater!"));
    }

    [Theory]
    [InlineData(false, "SeedPassw0rd!")]
    [InlineData(true, "")]
    [InlineData(true, "   ")]
    public async Task Nothing_is_seeded_without_both_the_switch_and_a_password(bool enabled, string password)
    {
        await using var context = _host.CreateContext();

        var created = await AuthDbSeeder.SeedAsync(
            context,
            new SeedOptions { Enabled = enabled, Password = password });

        Assert.Empty(created);
        Assert.Equal(0, await context.Users.CountAsync());
    }
}
