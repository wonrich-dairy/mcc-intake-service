using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Wonrich.Auth.Authorization;
using Wonrich.AuthService.Domain;
using Wonrich.AuthService.Infrastructure;

namespace Wonrich.AuthService.Application;

/// <summary>A user account as shown to an administrator. Never carries the password hash.</summary>
public sealed record UserView(
    Guid Id,
    string UserName,
    string DisplayName,
    string Role,
    string? Facility,
    bool IsActive);

/// <summary>Details supplied when creating an account (SCRUM-45).</summary>
public sealed record CreateUserCommand(
    string UserName,
    string DisplayName,
    string Password,
    string Role,
    string? Facility = null);

/// <summary>
/// Details supplied when amending an account. The username is not amendable: it is what the
/// sign-in log and every audit trail refer to, so moving it would strand that history.
/// </summary>
public sealed record UpdateUserCommand(
    string DisplayName,
    string Role,
    string? Facility = null,
    string? NewPassword = null);

/// <summary>Search and filter options for the account list (SCRUM-45).</summary>
public sealed record UserQuery
{
    /// <summary>Fragment matched against both username and display name.</summary>
    public string? Search { get; init; }

    /// <summary>When set, only accounts holding this role.</summary>
    public string? Role { get; init; }

    /// <summary>When false, deactivated accounts are listed alongside active ones.</summary>
    public bool ActiveOnly { get; init; }
}

/// <summary>Raised when a username is already taken.</summary>
public sealed class DuplicateUserNameException : Exception
{
    public DuplicateUserNameException(string userName)
        : base($"An account already exists with the username '{userName}'.")
    {
        UserName = userName;
    }

    public string UserName { get; }
}

/// <summary>Raised when an account is addressed that does not exist.</summary>
public sealed class UserNotFoundException : Exception
{
    public UserNotFoundException(Guid id)
        : base($"No account is registered under identifier '{id}'.")
    {
    }
}

/// <summary>Administration of user accounts and their role assignments (SCRUM-45).</summary>
public interface IUserService
{
    Task<IReadOnlyList<UserView>> ListAsync(UserQuery? query = null, CancellationToken cancellationToken = default);

    Task<UserView?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<UserView> CreateAsync(CreateUserCommand command, CancellationToken cancellationToken = default);

    Task<UserView> UpdateAsync(Guid id, UpdateUserCommand command, CancellationToken cancellationToken = default);

    Task<UserView> DeactivateAsync(Guid id, CancellationToken cancellationToken = default);

    Task<UserView> ReactivateAsync(Guid id, CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IUserService" />
public sealed class UserService : IUserService
{
    private readonly AuthDbContext _dbContext;

    public UserService(AuthDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<UserView>> ListAsync(
        UserQuery? query = null,
        CancellationToken cancellationToken = default)
    {
        query ??= new UserQuery();

        var users = _dbContext.Users.AsNoTracking().AsQueryable();

        if (query.ActiveOnly)
        {
            users = users.Where(user => user.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(query.Role))
        {
            users = users.Where(user => user.Role == query.Role);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            users = users.Where(user =>
                EF.Functions.Like(user.UserName, $"%{term}%") ||
                EF.Functions.Like(user.DisplayName, $"%{term}%"));
        }

        return await users
            .OrderBy(user => user.UserName)
            .Select(Projection)
            .ToListAsync(cancellationToken);
    }

    public async Task<UserView?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        await _dbContext.Users
            .AsNoTracking()
            .Where(user => user.Id == id)
            .Select(Projection)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<UserView> CreateAsync(
        CreateUserCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var user = new UserAccount(
            Guid.NewGuid(),
            command.UserName,
            command.DisplayName,
            command.Password,
            command.Role,
            command.Facility);

        await GuardUserNameIsFreeAsync(user.UserName, cancellationToken);

        _dbContext.Users.Add(user);
        await SaveGuardingUniqueUserNameAsync(user.UserName, cancellationToken);

        return ToView(user);
    }

    public async Task<UserView> UpdateAsync(
        Guid id,
        UpdateUserCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var user = await FindAsync(id, cancellationToken);

        user.Rename(command.DisplayName);
        user.ChangeRole(command.Role);
        user.MoveTo(command.Facility);

        if (!string.IsNullOrWhiteSpace(command.NewPassword))
        {
            user.ChangePassword(command.NewPassword);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return ToView(user);
    }

    public async Task<UserView> DeactivateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var user = await FindAsync(id, cancellationToken);

        user.Deactivate();

        // Sign-in is refused for a deactivated account, but any refresh token already issued
        // would otherwise keep working until it expired. Revoke them so access stops now.
        await RevokeRefreshTokensAsync(id, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return ToView(user);
    }

    public async Task<UserView> ReactivateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var user = await FindAsync(id, cancellationToken);

        user.Reactivate();
        await _dbContext.SaveChangesAsync(cancellationToken);

        return ToView(user);
    }

    private async Task RevokeRefreshTokensAsync(Guid userId, CancellationToken cancellationToken)
    {
        var tokens = await _dbContext.RefreshTokens
            .Where(token => token.UserId == userId && token.RevokedAtUtc == null)
            .ToListAsync(cancellationToken);

        foreach (var token in tokens)
        {
            token.Revoke(DateTime.UtcNow);
        }
    }

    private async Task<UserAccount> FindAsync(Guid id, CancellationToken cancellationToken) =>
        await _dbContext.Users.FirstOrDefaultAsync(user => user.Id == id, cancellationToken)
        ?? throw new UserNotFoundException(id);

    /// <summary>Rejects a taken username before the round trip, for a message that names it.</summary>
    private async Task GuardUserNameIsFreeAsync(string userName, CancellationToken cancellationToken)
    {
        var taken = await _dbContext.Users
            .AsNoTracking()
            .AnyAsync(user => user.UserName == userName, cancellationToken);

        if (taken)
        {
            throw new DuplicateUserNameException(userName);
        }
    }

    /// <summary>
    /// The pre-check closes the common case; the unique index settles a race between two
    /// administrators creating the same username at once.
    /// </summary>
    private async Task SaveGuardingUniqueUserNameAsync(string userName, CancellationToken cancellationToken)
    {
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueUserNameViolation(exception))
        {
            throw new DuplicateUserNameException(userName);
        }
    }

    private static bool IsUniqueUserNameViolation(DbUpdateException exception) =>
        exception.InnerException?.Message.Contains("ux_users_username", StringComparison.OrdinalIgnoreCase) == true
        || exception.InnerException?.Message.Contains(
            "UNIQUE constraint failed: users.UserName", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>Held as an expression so EF translates it into the SELECT list.</summary>
    private static readonly Expression<Func<UserAccount, UserView>> Projection = user => new UserView(
        user.Id,
        user.UserName,
        user.DisplayName,
        user.Role,
        user.Facility,
        user.IsActive);

    private static UserView ToView(UserAccount user) => new(
        user.Id,
        user.UserName,
        user.DisplayName,
        user.Role,
        user.Facility,
        user.IsActive);

    /// <summary>The roles an account may hold, for the administrator's role picker.</summary>
    public static IReadOnlyList<string> AssignableRoles => WonrichRoles.All;
}
