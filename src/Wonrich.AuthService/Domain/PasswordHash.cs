using System.Security.Cryptography;

namespace Wonrich.AuthService.Domain;

/// <summary>
/// A stored password, held as a PBKDF2-SHA256 hash with a per-password salt (SCRUM-34).
/// </summary>
/// <remarks>
/// Passwords are never stored or logged in the clear, and the hash is deliberately slow: the
/// iteration count is what makes a stolen table expensive to attack offline. The salt is per
/// password, so two users choosing the same password do not share a hash.
/// </remarks>
public sealed record PasswordHash(string Value)
{
    private const int SaltBytes = 16;
    private const int HashBytes = 32;

    /// <summary>OWASP's floor for PBKDF2-SHA256 at the time of writing.</summary>
    private const int Iterations = 210_000;

    /// <summary>Hashes a new password.</summary>
    public static PasswordHash From(string password)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            throw new ArgumentException("A password is required.", nameof(password));
        }

        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Derive(password, salt);

        return new PasswordHash(
            $"pbkdf2-sha256${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}");
    }

    /// <summary>
    /// Whether the supplied password produces this hash. Returns false rather than throwing on a
    /// malformed stored value, so a corrupted row denies access instead of failing the request.
    /// </summary>
    public bool Matches(string? password)
    {
        if (string.IsNullOrEmpty(password))
        {
            return false;
        }

        var parts = Value.Split('$');

        if (parts.Length != 4
            || parts[0] != "pbkdf2-sha256"
            || !int.TryParse(parts[1], out var iterations))
        {
            return false;
        }

        try
        {
            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            var actual = Derive(password, salt, iterations, expected.Length);

            // Fixed-time comparison: a byte-by-byte one leaks how much of the hash matched.
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static byte[] Derive(
        string password,
        byte[] salt,
        int iterations = Iterations,
        int length = HashBytes) =>
        Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, length);
}
