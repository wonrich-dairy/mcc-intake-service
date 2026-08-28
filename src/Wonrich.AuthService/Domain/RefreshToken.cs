using System.Security.Cryptography;
using System.Text;

namespace Wonrich.AuthService.Domain;

/// <summary>
/// A stored refresh token. Only a hash of the token is kept, so a leaked database cannot be used
/// to mint access tokens — the same reason passwords are not stored in the clear.
/// </summary>
public class RefreshToken
{
    /// <summary>EF Core materialisation constructor.</summary>
    private RefreshToken()
    {
        TokenHash = string.Empty;
    }

    public RefreshToken(Guid id, Guid userId, string token, DateTime expiresAtUtc)
    {
        Id = id;
        UserId = userId;
        TokenHash = Hash(token);
        ExpiresAtUtc = expiresAtUtc;
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public UserAccount? User { get; private set; }

    /// <summary>SHA-256 of the issued token. The token itself is only ever held by the client.</summary>
    public string TokenHash { get; private set; }

    public DateTime ExpiresAtUtc { get; private set; }

    /// <summary>Set when the token is spent or explicitly revoked.</summary>
    public DateTime? RevokedAtUtc { get; private set; }

    /// <summary>Whether this token can still be exchanged for a new access token.</summary>
    public bool IsUsable(DateTime nowUtc) => RevokedAtUtc is null && ExpiresAtUtc > nowUtc;

    /// <summary>
    /// Marks the token as spent. Refresh tokens are single-use: exchanging one revokes it and
    /// issues a replacement, so a stolen token stops working as soon as the real user refreshes.
    /// </summary>
    public void Revoke(DateTime nowUtc) => RevokedAtUtc ??= nowUtc;

    /// <summary>Hashes a token for storage and lookup. A plain SHA-256 is enough here because
    /// the token is 256 bits of randomness, not a guessable secret like a password.</summary>
    public static string Hash(string token) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
