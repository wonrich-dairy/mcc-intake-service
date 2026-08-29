using System.Security.Claims;

namespace Wonrich.Auth.Tokens;

/// <summary>Claim types the Wonrich services agree on, beyond the standard JWT registered ones.</summary>
public static class WonrichClaims
{
    /// <summary>Chilling centre or factory the user operates at.</summary>
    public const string Facility = "facility";
}

/// <summary>
/// Reads the Wonrich claims off an authenticated principal, so endpoints and services do not each
/// re-derive which claim type carries what.
/// </summary>
public static class WonrichPrincipalExtensions
{
    /// <summary>The user's identifier, taken from the token subject.</summary>
    public static string? UserId(this ClaimsPrincipal principal) =>
        principal.FindFirstValue(ClaimTypes.NameIdentifier);

    /// <summary>The user's sign-in name.</summary>
    public static string? UserName(this ClaimsPrincipal principal) =>
        principal.FindFirstValue(ClaimTypes.Name);

    /// <summary>The facility the user operates at.</summary>
    public static string? Facility(this ClaimsPrincipal principal) =>
        principal.FindFirstValue(WonrichClaims.Facility);

    /// <summary>The single role the user holds. Users carry exactly one role (SCRUM-45).</summary>
    public static string? Role(this ClaimsPrincipal principal) =>
        principal.FindFirstValue(ClaimTypes.Role);
}
