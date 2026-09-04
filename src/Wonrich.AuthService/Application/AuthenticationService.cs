using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Wonrich.Auth.Tokens;
using Wonrich.AuthService.Domain;
using Wonrich.AuthService.Infrastructure;

namespace Wonrich.AuthService.Application;

/// <summary>Why a sign-in or refresh was refused.</summary>
public enum AuthFailure
{
    /// <summary>The username is unknown or the password is wrong.</summary>
    InvalidCredentials,

    /// <summary>The account exists but has been deactivated.</summary>
    AccountDeactivated,

    /// <summary>The refresh token is unknown, already spent, revoked or expired.</summary>
    InvalidRefreshToken
}

/// <summary>Outcome of a sign-in or refresh.</summary>
/// <param name="Tokens">The issued tokens when the attempt succeeded.</param>
/// <param name="Failure">Why the attempt was refused, when it was.</param>
/// <summary>Who the signed-in user is, for a client that has to name them on screen.</summary>
/// <param name="UserName">Sign-in name.</param>
/// <param name="DisplayName">The name a person is called, as an officer would read it.</param>
/// <param name="Role">The single role the user holds (SCRUM-45).</param>
/// <param name="Facility">Centre or factory the user operates at.</param>
public sealed record SignedInUser(string UserName, string DisplayName, string Role, string? Facility);

public sealed record AuthResult(IssuedTokens? Tokens, SignedInUser? User, AuthFailure? Failure)
{
    public bool Succeeded => Tokens is not null;

    public static AuthResult Success(IssuedTokens tokens, SignedInUser user) => new(tokens, user, null);

    public static AuthResult Refused(AuthFailure failure) => new(null, null, failure);
}

/// <summary>Issues and renews tokens for Wonrich users (SCRUM-34).</summary>
public interface IAuthenticationService
{
    /// <summary>Signs a user in with a username and password.</summary>
    Task<AuthResult> SignInAsync(
        string userName,
        string password,
        string? source,
        CancellationToken cancellationToken = default);

    /// <summary>Exchanges a refresh token for a new pair of tokens.</summary>
    Task<AuthResult> RefreshAsync(
        string refreshToken,
        string? source,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IAuthenticationService" />
public sealed class AuthenticationService : IAuthenticationService
{
    private readonly AuthDbContext _dbContext;
    private readonly IAccessTokenIssuer _issuer;
    private readonly WonrichJwtOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<AuthenticationService> _logger;

    public AuthenticationService(
        AuthDbContext dbContext,
        IAccessTokenIssuer issuer,
        IOptions<WonrichJwtOptions> options,
        TimeProvider time,
        ILogger<AuthenticationService> logger)
    {
        _dbContext = dbContext;
        _issuer = issuer;
        _options = options.Value;
        _time = time;
        _logger = logger;
    }

    public async Task<AuthResult> SignInAsync(
        string userName,
        string password,
        string? source,
        CancellationToken cancellationToken = default)
    {
        var normalised = string.IsNullOrWhiteSpace(userName)
            ? string.Empty
            : userName.Trim().ToLowerInvariant();

        var user = await _dbContext.Users
            .FirstOrDefaultAsync(account => account.UserName == normalised, cancellationToken);

        if (user is null || !user.CanSignInWith(password))
        {
            // Deliberately one outcome for "no such user" and "wrong password": telling them
            // apart would let an attacker enumerate valid usernames.
            var failure = user is { IsActive: false }
                ? AuthFailure.AccountDeactivated
                : AuthFailure.InvalidCredentials;

            LogFailedAttempt(normalised, source, failure);

            return AuthResult.Refused(AuthFailure.InvalidCredentials);
        }

        return AuthResult.Success(await IssueForAsync(user, cancellationToken), Describe(user));
    }

    public async Task<AuthResult> RefreshAsync(
        string refreshToken,
        string? source,
        CancellationToken cancellationToken = default)
    {
        var nowUtc = _time.GetUtcNow().UtcDateTime;
        var hash = RefreshToken.Hash(refreshToken ?? string.Empty);

        var stored = await _dbContext.RefreshTokens
            .Include(record => record.User)
            .FirstOrDefaultAsync(record => record.TokenHash == hash, cancellationToken);

        if (stored is null || !stored.IsUsable(nowUtc) || stored.User is not { IsActive: true })
        {
            _logger.LogWarning(
                "Refresh refused at {Timestamp:o} from {Source}: token unknown, spent or expired.",
                nowUtc,
                source ?? "unknown");

            return AuthResult.Refused(AuthFailure.InvalidRefreshToken);
        }

        // Single use: spend the presented token before handing out its replacement, so a stolen
        // copy stops working the moment the real user refreshes.
        stored.Revoke(nowUtc);

        return AuthResult.Success(await IssueForAsync(stored.User, cancellationToken), Describe(stored.User));
    }

    private static SignedInUser Describe(UserAccount user) =>
        new(user.UserName, user.DisplayName, user.Role, user.Facility);

    private async Task<IssuedTokens> IssueForAsync(UserAccount user, CancellationToken cancellationToken)
    {
        var tokens = _issuer.Issue(new TokenSubject(
            user.Id.ToString(),
            user.UserName,
            user.Facility,
            user.Role));

        _dbContext.RefreshTokens.Add(new RefreshToken(
            Guid.NewGuid(),
            user.Id,
            tokens.RefreshToken,
            tokens.RefreshExpiresAtUtc));

        await _dbContext.SaveChangesAsync(cancellationToken);

        return tokens;
    }

    /// <summary>
    /// SCRUM-34 requires failed sign-ins to be recorded with the username, the time and where
    /// they came from. The password is never part of that record.
    /// </summary>
    private void LogFailedAttempt(string userName, string? source, AuthFailure failure) =>
        _logger.LogWarning(
            "Failed sign-in for {UserName} at {Timestamp:o} from {Source}: {Reason}.",
            string.IsNullOrEmpty(userName) ? "(blank)" : userName,
            _time.GetUtcNow().UtcDateTime,
            source ?? "unknown",
            failure);
}
