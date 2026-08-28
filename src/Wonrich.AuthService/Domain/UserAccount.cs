using Wonrich.Auth.Authorization;

namespace Wonrich.AuthService.Domain;

/// <summary>
/// A user of the Wonrich services. Accounts are deactivated rather than deleted so that history
/// referring to them keeps resolving (SCRUM-45).
/// </summary>
public class UserAccount
{
    public const int MaxUserNameLength = 100;
    public const int MaxDisplayNameLength = 150;
    public const int MaxFacilityLength = 50;

    /// <summary>EF Core materialisation constructor.</summary>
    private UserAccount()
    {
        UserName = string.Empty;
        DisplayName = string.Empty;
        PasswordHash = string.Empty;
        Role = string.Empty;
    }

    public UserAccount(
        Guid id,
        string userName,
        string displayName,
        string password,
        string role,
        string? facility = null)
    {
        Id = id;
        UserName = NormaliseUserName(userName);
        DisplayName = Require(displayName, MaxDisplayNameLength, nameof(displayName));
        PasswordHash = Domain.PasswordHash.From(password).Value;
        Role = EnsureConfigured(role);
        Facility = string.IsNullOrWhiteSpace(facility)
            ? null
            : Require(facility, MaxFacilityLength, nameof(facility));
        IsActive = true;
    }

    public Guid Id { get; private set; }

    /// <summary>Sign-in name, unique and compared case-insensitively.</summary>
    public string UserName { get; private set; }

    public string DisplayName { get; private set; }

    /// <summary>The PBKDF2 hash; never the password itself.</summary>
    public string PasswordHash { get; private set; }

    /// <summary>The single role this user holds, from the configured seven (SCRUM-45).</summary>
    public string Role { get; private set; }

    /// <summary>Chilling centre or factory the user operates at.</summary>
    public string? Facility { get; private set; }

    /// <summary>Deactivated accounts cannot log in but remain visible in history.</summary>
    public bool IsActive { get; private set; }

    /// <summary>Whether this account may sign in with the supplied password.</summary>
    public bool CanSignInWith(string? password) =>
        IsActive && new PasswordHash(PasswordHash).Matches(password);

    /// <summary>Moves the account to a different configured role.</summary>
    /// <remarks>A user holds exactly one role at a time, so this replaces rather than adds.</remarks>
    public void ChangeRole(string role) => Role = EnsureConfigured(role);

    /// <summary>Corrects the display name. The username itself never moves.</summary>
    public void Rename(string displayName) =>
        DisplayName = Require(displayName, MaxDisplayNameLength, nameof(displayName));

    /// <summary>Reassigns the account to a different facility, or to none.</summary>
    public void MoveTo(string? facility) =>
        Facility = string.IsNullOrWhiteSpace(facility)
            ? null
            : Require(facility, MaxFacilityLength, nameof(facility));

    /// <summary>Replaces the stored password.</summary>
    public void ChangePassword(string password) => PasswordHash = Domain.PasswordHash.From(password).Value;

    /// <summary>Retires the account so it can no longer sign in.</summary>
    public void Deactivate() => IsActive = false;

    /// <summary>Returns a retired account to service.</summary>
    public void Reactivate() => IsActive = true;

    /// <summary>Sign-in names are stored lower-cased so a login cannot depend on typing the case.</summary>
    public static string NormaliseUserName(string userName) =>
        Require(userName, MaxUserNameLength, nameof(userName)).ToLowerInvariant();

    private static string EnsureConfigured(string role) =>
        WonrichRoles.IsConfigured(role)
            ? role
            : throw new ArgumentException(
                $"'{role}' is not one of the seven configured Wonrich roles.", nameof(role));

    private static string Require(string value, int maxLength, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{field} is required.", field);
        }

        var trimmed = value.Trim();

        return trimmed.Length > maxLength
            ? throw new ArgumentException($"{field} cannot exceed {maxLength} characters.", field)
            : trimmed;
    }
}
