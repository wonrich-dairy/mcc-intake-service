using Microsoft.EntityFrameworkCore;
using Wonrich.Auth.Authorization;
using Wonrich.AuthService.Application;
using Wonrich.AuthService.Domain;
using Wonrich.AuthService.Tests.Support;

namespace Wonrich.AuthService.Tests.Application;

/// <summary>Covers sign-in, refresh and the rules around them (SCRUM-34).</summary>
public class AuthenticationServiceTests : IDisposable
{
    private readonly AuthTestHost _host = AuthTestHost.Create();

    [Fact]
    public async Task Correct_credentials_are_accepted_and_issue_both_tokens()
    {
        _host.SeedUser();

        await using var context = _host.CreateContext();
        var result = await _host.CreateService(context)
            .SignInAsync("k.perera", AuthTestHost.KnownPassword, "10.0.0.1");

        Assert.True(result.Succeeded);
        Assert.NotEmpty(result.Tokens!.AccessToken);
        Assert.NotEmpty(result.Tokens.RefreshToken);
    }

    [Fact]
    public async Task The_username_is_matched_regardless_of_case()
    {
        _host.SeedUser();

        await using var context = _host.CreateContext();
        var result = await _host.CreateService(context)
            .SignInAsync("K.Perera", AuthTestHost.KnownPassword, null);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task A_wrong_password_is_refused()
    {
        _host.SeedUser();

        await using var context = _host.CreateContext();
        var result = await _host.CreateService(context).SignInAsync("k.perera", "wrong", "10.0.0.1");

        Assert.False(result.Succeeded);
        Assert.Equal(AuthFailure.InvalidCredentials, result.Failure);
    }

    [Fact]
    public async Task An_unknown_user_is_refused_the_same_way_as_a_wrong_password()
    {
        _host.SeedUser();

        await using var context = _host.CreateContext();
        var service = _host.CreateService(context);

        var unknown = await service.SignInAsync("nobody", AuthTestHost.KnownPassword, null);
        var wrongPassword = await service.SignInAsync("k.perera", "wrong", null);

        // Identical outcomes: telling them apart would let a caller enumerate usernames.
        Assert.Equal(wrongPassword.Failure, unknown.Failure);
    }

    [Fact]
    public async Task A_deactivated_account_cannot_sign_in()
    {
        _host.SeedUser(active: false);

        await using var context = _host.CreateContext();
        var result = await _host.CreateService(context)
            .SignInAsync("k.perera", AuthTestHost.KnownPassword, null);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task A_refresh_token_buys_a_new_pair_of_tokens()
    {
        _host.SeedUser();

        await using var context = _host.CreateContext();
        var service = _host.CreateService(context);
        var signedIn = await service.SignInAsync("k.perera", AuthTestHost.KnownPassword, null);

        _host.Time.Advance(TimeSpan.FromMinutes(5));
        var refreshed = await service.RefreshAsync(signedIn.Tokens!.RefreshToken, null);

        Assert.True(refreshed.Succeeded);
        Assert.NotEqual(signedIn.Tokens.RefreshToken, refreshed.Tokens!.RefreshToken);
    }

    [Fact]
    public async Task A_refresh_token_cannot_be_used_twice()
    {
        _host.SeedUser();

        await using var context = _host.CreateContext();
        var service = _host.CreateService(context);
        var signedIn = await service.SignInAsync("k.perera", AuthTestHost.KnownPassword, null);

        var first = await service.RefreshAsync(signedIn.Tokens!.RefreshToken, null);
        var second = await service.RefreshAsync(signedIn.Tokens.RefreshToken, null);

        // Single use is what makes a stolen refresh token stop working once the real user refreshes.
        Assert.True(first.Succeeded);
        Assert.False(second.Succeeded);
        Assert.Equal(AuthFailure.InvalidRefreshToken, second.Failure);
    }

    [Fact]
    public async Task An_expired_refresh_token_is_refused()
    {
        _host.SeedUser();

        await using var context = _host.CreateContext();
        var service = _host.CreateService(context);
        var signedIn = await service.SignInAsync("k.perera", AuthTestHost.KnownPassword, null);

        _host.Time.Advance(TimeSpan.FromDays(_host.Options.RefreshTokenDays) + TimeSpan.FromMinutes(1));
        var refreshed = await service.RefreshAsync(signedIn.Tokens!.RefreshToken, null);

        Assert.False(refreshed.Succeeded);
    }

    [Fact]
    public async Task An_unknown_refresh_token_is_refused()
    {
        await using var context = _host.CreateContext();

        var result = await _host.CreateService(context).RefreshAsync("not-a-real-token", null);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task A_refresh_token_stops_working_once_its_account_is_deactivated()
    {
        var user = _host.SeedUser();

        await using var context = _host.CreateContext();
        var service = _host.CreateService(context);
        var signedIn = await service.SignInAsync("k.perera", AuthTestHost.KnownPassword, null);

        await using (var admin = _host.CreateContext())
        {
            var account = await admin.Users.SingleAsync(candidate => candidate.Id == user.Id);
            account.Deactivate();
            await admin.SaveChangesAsync();
        }

        await using var fresh = _host.CreateContext();
        var refreshed = await _host.CreateService(fresh).RefreshAsync(signedIn.Tokens!.RefreshToken, null);

        Assert.False(refreshed.Succeeded);
    }

    [Fact]
    public async Task The_refresh_token_is_never_stored_in_the_clear()
    {
        _host.SeedUser();

        await using var context = _host.CreateContext();
        var signedIn = await _host.CreateService(context)
            .SignInAsync("k.perera", AuthTestHost.KnownPassword, null);

        await using var verification = _host.CreateContext();
        var stored = await verification.RefreshTokens.SingleAsync();

        Assert.NotEqual(signedIn.Tokens!.RefreshToken, stored.TokenHash);
        Assert.Equal(RefreshToken.Hash(signedIn.Tokens.RefreshToken), stored.TokenHash);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_blank_username_is_refused(string userName)
    {
        await using var context = _host.CreateContext();

        var result = await _host.CreateService(context).SignInAsync(userName, "whatever", null);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public void An_account_cannot_hold_a_role_outside_the_configured_seven()
    {
        Assert.Throws<ArgumentException>(
            () => new UserAccount(Guid.NewGuid(), "x", "X", "password", "NotARole"));
    }

    [Fact]
    public void An_account_can_be_moved_between_configured_roles()
    {
        var user = new UserAccount(
            Guid.NewGuid(), "x", "X", "password", WonrichRoles.IntakeOfficer);

        user.ChangeRole(WonrichRoles.QualityAnalyst);

        Assert.Equal(WonrichRoles.QualityAnalyst, user.Role);
        Assert.Throws<ArgumentException>(() => user.ChangeRole("NotARole"));
    }

    [Fact]
    public void A_changed_password_replaces_the_old_one()
    {
        var user = new UserAccount(
            Guid.NewGuid(), "x", "X", "first-password", WonrichRoles.IntakeOfficer);

        user.ChangePassword("second-password");

        Assert.False(user.CanSignInWith("first-password"));
        Assert.True(user.CanSignInWith("second-password"));
    }

    [Fact]
    public void A_reactivated_account_can_sign_in_again()
    {
        var user = new UserAccount(
            Guid.NewGuid(), "x", "X", "password", WonrichRoles.IntakeOfficer);

        user.Deactivate();
        Assert.False(user.CanSignInWith("password"));

        user.Reactivate();
        Assert.True(user.CanSignInWith("password"));
    }

    public void Dispose()
    {
        _host.Dispose();
        GC.SuppressFinalize(this);
    }
}
