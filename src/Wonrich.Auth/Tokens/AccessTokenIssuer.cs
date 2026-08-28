using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Wonrich.Auth.Tokens;

/// <summary>Identity a token is issued for.</summary>
/// <param name="UserId">Stable identifier of the user account.</param>
/// <param name="UserName">Sign-in name, carried so services can log who acted.</param>
/// <param name="Facility">Chilling centre or factory the user operates at.</param>
/// <param name="Role">The single role the user holds.</param>
public sealed record TokenSubject(string UserId, string UserName, string? Facility, string Role);

/// <summary>A freshly issued access token and the refresh token that renews it.</summary>
/// <param name="AccessToken">Signed JWT presented as a bearer token.</param>
/// <param name="ExpiresAtUtc">Instant the access token stops being accepted.</param>
/// <param name="RefreshToken">Opaque token exchanged for a new access token.</param>
/// <param name="RefreshExpiresAtUtc">Instant the refresh token stops being accepted.</param>
public sealed record IssuedTokens(
    string AccessToken,
    DateTime ExpiresAtUtc,
    string RefreshToken,
    DateTime RefreshExpiresAtUtc);

/// <summary>Issues the signed tokens the Wonrich services accept (SCRUM-34).</summary>
public interface IAccessTokenIssuer
{
    /// <summary>Issues an access token and a refresh token for the given user.</summary>
    IssuedTokens Issue(TokenSubject subject);
}

/// <inheritdoc cref="IAccessTokenIssuer" />
public sealed class AccessTokenIssuer : IAccessTokenIssuer
{
    private readonly WonrichJwtOptions _options;
    private readonly TimeProvider _time;

    public AccessTokenIssuer(IOptions<WonrichJwtOptions> options, TimeProvider time)
    {
        _options = options.Value;
        _time = time;
    }

    public IssuedTokens Issue(TokenSubject subject)
    {
        ArgumentNullException.ThrowIfNull(subject);

        if (!Authorization.WonrichRoles.IsConfigured(subject.Role))
        {
            // Refusing here keeps an unknown role from reaching a token, where every service
            // would then have to decide what to do with a role none of their policies name.
            throw new InvalidOperationException(
                $"'{subject.Role}' is not one of the configured Wonrich roles.");
        }

        var issuedAt = _time.GetUtcNow().UtcDateTime;
        var expiresAt = issuedAt.AddMinutes(_options.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, subject.UserId),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(ClaimTypes.NameIdentifier, subject.UserId),
            new(ClaimTypes.Name, subject.UserName),
            new(ClaimTypes.Role, subject.Role)
        };

        if (!string.IsNullOrWhiteSpace(subject.Facility))
        {
            claims.Add(new Claim(WonrichClaims.Facility, subject.Facility));
        }

        var credentials = new SigningCredentials(
            SigningKeyFor(_options),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: issuedAt,
            expires: expiresAt,
            signingCredentials: credentials);

        return new IssuedTokens(
            new JwtSecurityTokenHandler().WriteToken(token),
            expiresAt,
            NewRefreshToken(),
            issuedAt.AddDays(_options.RefreshTokenDays));
    }

    /// <summary>
    /// The refresh token is opaque rather than a second JWT: it carries no claims, so it cannot
    /// be replayed as an access token even if a service mistakenly accepts it as one.
    /// </summary>
    private static string NewRefreshToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    /// <summary>The signing key both issuance and validation derive from the configured secret.</summary>
    internal static SymmetricSecurityKey SigningKeyFor(WonrichJwtOptions options) =>
        new(System.Text.Encoding.UTF8.GetBytes(options.SigningKey));
}
