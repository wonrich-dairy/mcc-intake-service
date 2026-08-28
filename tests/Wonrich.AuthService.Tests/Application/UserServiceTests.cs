using Microsoft.EntityFrameworkCore;
using Wonrich.Auth.Authorization;
using Wonrich.AuthService.Application;
using Wonrich.AuthService.Tests.Support;

namespace Wonrich.AuthService.Tests.Application;

/// <summary>Covers the account administration delivered by SCRUM-45.</summary>
public class UserServiceTests : IDisposable
{
    private readonly AuthTestHost _host = AuthTestHost.Create();

    private UserService CreateService(out Wonrich.AuthService.Infrastructure.AuthDbContext context)
    {
        context = _host.CreateContext();

        return new UserService(context);
    }

    private static CreateUserCommand NewUser(
        string userName = "n.silva",
        string role = WonrichRoles.IntakeOfficer) =>
        new(userName, "Nimal Silva", "a-long-enough-password", role, "MCC-KANDY");

    [Fact]
    public async Task An_account_can_be_created_and_then_fetched()
    {
        var service = CreateService(out var context);
        await using var _ = context;

        var created = await service.CreateAsync(NewUser());

        Assert.Equal("n.silva", created.UserName);
        Assert.Equal(WonrichRoles.IntakeOfficer, created.Role);
        Assert.True(created.IsActive);

        var fetched = await service.GetAsync(created.Id);
        Assert.Equal(created.Id, fetched!.Id);
    }

    [Fact]
    public async Task A_username_is_stored_lower_cased()
    {
        var service = CreateService(out var context);
        await using var _ = context;

        var created = await service.CreateAsync(NewUser("N.Silva"));

        Assert.Equal("n.silva", created.UserName);
    }

    [Fact]
    public async Task A_duplicate_username_is_rejected_regardless_of_casing()
    {
        var service = CreateService(out var context);
        await using var _ = context;

        await service.CreateAsync(NewUser());

        await Assert.ThrowsAsync<DuplicateUserNameException>(() => service.CreateAsync(NewUser("N.SILVA")));
    }

    [Fact]
    public async Task A_role_outside_the_configured_seven_is_rejected()
    {
        var service = CreateService(out var context);
        await using var _ = context;

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.CreateAsync(NewUser(role: "Chief Cheese Taster")));
    }

    [Fact]
    public async Task An_account_can_be_amended_and_moved_between_roles()
    {
        var service = CreateService(out var context);
        await using var _ = context;

        var created = await service.CreateAsync(NewUser());

        var updated = await service.UpdateAsync(
            created.Id,
            new UpdateUserCommand("Nimal T. Silva", WonrichRoles.QualityAnalyst, "MCC-NUWARA"));

        Assert.Equal("Nimal T. Silva", updated.DisplayName);
        Assert.Equal(WonrichRoles.QualityAnalyst, updated.Role);
        Assert.Equal("MCC-NUWARA", updated.Facility);

        // Exactly one role at a time: the new assignment replaces the old.
        Assert.NotEqual(WonrichRoles.IntakeOfficer, updated.Role);
    }

    [Fact]
    public async Task The_username_is_not_amendable()
    {
        var service = CreateService(out var context);
        await using var _ = context;

        var created = await service.CreateAsync(NewUser());

        var updated = await service.UpdateAsync(
            created.Id,
            new UpdateUserCommand("Renamed", WonrichRoles.IntakeOfficer));

        Assert.Equal("n.silva", updated.UserName);
    }

    [Fact]
    public async Task A_password_reset_replaces_the_old_password()
    {
        var service = CreateService(out var context);
        await using var _ = context;

        var created = await service.CreateAsync(NewUser());

        await service.UpdateAsync(
            created.Id,
            new UpdateUserCommand("Nimal Silva", WonrichRoles.IntakeOfficer, null, "a-brand-new-password"));

        await using var verification = _host.CreateContext();
        var account = await verification.Users.SingleAsync(user => user.Id == created.Id);

        Assert.False(account.CanSignInWith("a-long-enough-password"));
        Assert.True(account.CanSignInWith("a-brand-new-password"));
    }

    [Fact]
    public async Task Omitting_a_new_password_leaves_the_existing_one_alone()
    {
        var service = CreateService(out var context);
        await using var _ = context;

        var created = await service.CreateAsync(NewUser());

        await service.UpdateAsync(created.Id, new UpdateUserCommand("Nimal Silva", WonrichRoles.IntakeOfficer));

        await using var verification = _host.CreateContext();
        var account = await verification.Users.SingleAsync(user => user.Id == created.Id);

        Assert.True(account.CanSignInWith("a-long-enough-password"));
    }

    [Fact]
    public async Task A_deactivated_account_stays_visible_but_drops_out_of_the_active_list()
    {
        var service = CreateService(out var context);
        await using var _ = context;

        var created = await service.CreateAsync(NewUser());
        await service.DeactivateAsync(created.Id);

        var active = await service.ListAsync(new UserQuery { ActiveOnly = true });
        var all = await service.ListAsync();

        Assert.DoesNotContain(active, user => user.UserName == "n.silva");
        Assert.Contains(all, user => user.UserName == "n.silva");
        Assert.False((await service.GetAsync(created.Id))!.IsActive);
    }

    [Fact]
    public async Task Deactivating_an_account_revokes_its_outstanding_refresh_tokens()
    {
        _host.SeedUser();

        await using var authContext = _host.CreateContext();
        var signedIn = await _host.CreateService(authContext)
            .SignInAsync("k.perera", AuthTestHost.KnownPassword, null);

        var account = await authContext.Users.SingleAsync(user => user.UserName == "k.perera");

        await using (var admin = _host.CreateContext())
        {
            await new UserService(admin).DeactivateAsync(account.Id);
        }

        // Sign-in already refused a deactivated account; the token it held must stop too.
        await using var fresh = _host.CreateContext();
        var refreshed = await _host.CreateService(fresh).RefreshAsync(signedIn.Tokens!.RefreshToken, null);

        Assert.False(refreshed.Succeeded);
    }

    [Fact]
    public async Task A_deactivated_account_can_be_returned_to_service()
    {
        var service = CreateService(out var context);
        await using var _ = context;

        var created = await service.CreateAsync(NewUser());
        await service.DeactivateAsync(created.Id);

        var reactivated = await service.ReactivateAsync(created.Id);

        Assert.True(reactivated.IsActive);
    }

    [Fact]
    public async Task The_list_can_be_searched_by_username_and_display_name()
    {
        var service = CreateService(out var context);
        await using var _ = context;

        await service.CreateAsync(NewUser());
        await service.CreateAsync(new CreateUserCommand(
            "s.fernando", "Sunil Fernando", "another-long-password", WonrichRoles.BowserOperator));

        Assert.Equal("n.silva", Assert.Single(await service.ListAsync(new UserQuery { Search = "silva" })).UserName);
        Assert.Equal("s.fernando", Assert.Single(await service.ListAsync(new UserQuery { Search = "Sunil" })).UserName);
    }

    [Fact]
    public async Task The_list_can_be_filtered_by_role()
    {
        var service = CreateService(out var context);
        await using var _ = context;

        await service.CreateAsync(NewUser());
        await service.CreateAsync(new CreateUserCommand(
            "s.fernando", "Sunil Fernando", "another-long-password", WonrichRoles.BowserOperator));

        var operators = await service.ListAsync(new UserQuery { Role = WonrichRoles.BowserOperator });

        Assert.Equal("s.fernando", Assert.Single(operators).UserName);
    }

    [Fact]
    public async Task The_list_is_ordered_by_username()
    {
        var service = CreateService(out var context);
        await using var _ = context;

        await service.CreateAsync(NewUser("z.perera"));
        await service.CreateAsync(NewUser("a.bandara"));

        var users = await service.ListAsync();

        Assert.Equal(["a.bandara", "z.perera"], users.Select(user => user.UserName));
    }

    [Fact]
    public async Task Addressing_an_account_that_does_not_exist_is_rejected()
    {
        var service = CreateService(out var context);
        await using var _ = context;

        Assert.Null(await service.GetAsync(Guid.NewGuid()));
        await Assert.ThrowsAsync<UserNotFoundException>(() => service.DeactivateAsync(Guid.NewGuid()));
        await Assert.ThrowsAsync<UserNotFoundException>(() => service.ReactivateAsync(Guid.NewGuid()));
        await Assert.ThrowsAsync<UserNotFoundException>(
            () => service.UpdateAsync(Guid.NewGuid(), new UpdateUserCommand("X", WonrichRoles.IntakeOfficer)));
    }

    [Fact]
    public async Task A_view_never_carries_the_password_hash()
    {
        var service = CreateService(out var context);
        await using var _ = context;

        var created = await service.CreateAsync(NewUser());

        // UserView has no hash property at all; assert the value cannot be reached through it.
        Assert.DoesNotContain(
            nameof(Wonrich.AuthService.Domain.UserAccount.PasswordHash),
            created.GetType().GetProperties().Select(property => property.Name));
    }

    [Fact]
    public void All_seven_roles_are_assignable()
    {
        Assert.Equal(7, UserService.AssignableRoles.Count);
        Assert.Equal(WonrichRoles.All, UserService.AssignableRoles);
    }

    public void Dispose()
    {
        _host.Dispose();
        GC.SuppressFinalize(this);
    }
}
