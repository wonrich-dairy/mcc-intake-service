using Wonrich.AuthService.Domain;

namespace Wonrich.AuthService.Tests.Domain;

public class PasswordHashTests
{
    [Fact]
    public void A_password_matches_its_own_hash()
    {
        var hash = PasswordHash.From("correct-horse-battery-staple");

        Assert.True(hash.Matches("correct-horse-battery-staple"));
    }

    [Fact]
    public void A_different_password_does_not_match()
    {
        var hash = PasswordHash.From("correct-horse-battery-staple");

        Assert.False(hash.Matches("Correct-Horse-Battery-Staple"));
        Assert.False(hash.Matches("something-else"));
    }

    [Fact]
    public void The_password_never_appears_in_the_stored_value()
    {
        var hash = PasswordHash.From("correct-horse-battery-staple");

        Assert.DoesNotContain("correct-horse-battery-staple", hash.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void The_same_password_hashes_differently_each_time()
    {
        // Per-password salt: two users choosing the same password must not share a hash.
        var first = PasswordHash.From("shared-password");
        var second = PasswordHash.From("shared-password");

        Assert.NotEqual(first.Value, second.Value);
        Assert.True(first.Matches("shared-password"));
        Assert.True(second.Matches("shared-password"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void An_empty_candidate_never_matches(string? candidate)
    {
        Assert.False(PasswordHash.From("a-real-password").Matches(candidate));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_password_cannot_be_hashed(string password)
    {
        Assert.Throws<ArgumentException>(() => PasswordHash.From(password));
    }

    [Theory]
    [InlineData("not-a-hash")]
    [InlineData("pbkdf2-sha256$notanumber$c2FsdA==$aGFzaA==")]
    [InlineData("pbkdf2-sha256$210000$!!!not-base64!!!$aGFzaA==")]
    [InlineData("bcrypt$210000$c2FsdA==$aGFzaA==")]
    public void A_malformed_stored_hash_denies_access_rather_than_throwing(string stored)
    {
        // A corrupted row must fail closed: the request is denied, not turned into a 500.
        Assert.False(new PasswordHash(stored).Matches("any-password"));
    }
}
