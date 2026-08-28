using System.ComponentModel.DataAnnotations;

namespace Wonrich.Auth.Tokens;

/// <summary>
/// Token settings shared by the service that issues JWTs and every service that validates them,
/// bound from the "Auth" configuration section (SCRUM-34).
/// </summary>
public sealed class WonrichJwtOptions
{
    public const string SectionName = "Auth";

    /// <summary>Shortest signing key that keeps HMAC-SHA256 at full strength.</summary>
    public const int MinimumSigningKeyLength = 32;

    /// <summary>Issuer stamped on every token and required by every validator.</summary>
    [Required]
    public string Issuer { get; set; } = "wonrich-auth";

    /// <summary>Audience the Wonrich services accept. All four share one audience.</summary>
    [Required]
    public string Audience { get; set; } = "wonrich-services";

    /// <summary>
    /// Symmetric signing key. Supplied per environment and never committed — the services read it
    /// from configuration, which in staging and production comes from the environment.
    /// </summary>
    [Required(ErrorMessage = "Auth:SigningKey is required.")]
    [MinLength(
        MinimumSigningKeyLength,
        ErrorMessage = "Auth:SigningKey must be at least 32 characters so HMAC-SHA256 is not weakened.")]
    public string SigningKey { get; set; } = string.Empty;

    /// <summary>How long an access token stays valid. Kept short because it cannot be revoked.</summary>
    [Range(1, 1440, ErrorMessage = "Auth:AccessTokenMinutes must be between 1 and 1440.")]
    public int AccessTokenMinutes { get; set; } = 60;

    /// <summary>
    /// How long a refresh token stays valid. Long enough to cover an intake officer's shift
    /// without a second login, short enough that a stolen one expires on its own.
    /// </summary>
    [Range(1, 90, ErrorMessage = "Auth:RefreshTokenDays must be between 1 and 90.")]
    public int RefreshTokenDays { get; set; } = 7;

    /// <summary>
    /// Tolerance allowed when comparing token lifetimes against the clock. Deliberately zero:
    /// the default five minutes would let an expired token keep working past its stated expiry.
    /// </summary>
    public TimeSpan ClockSkew { get; set; } = TimeSpan.Zero;
}
