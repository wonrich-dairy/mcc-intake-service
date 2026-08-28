using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.Extensions.Options;
using Wonrich.Auth.Authorization;
using Wonrich.Auth.Tokens;
using Wonrich.AuthService.Tests.Support;

namespace Wonrich.AuthService.Tests.Tokens;

/// <summary>Covers what a token carries and how long it lives (SCRUM-34).</summary>
public class AccessTokenIssuerTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 28, 8, 0, 0, TimeSpan.Zero);

    private static readonly WonrichJwtOptions Options = new()
    {
        Issuer = "wonrich-auth-tests",
        Audience = "wonrich-services-tests",
        SigningKey = AuthTestHost.SigningKey,
        AccessTokenMinutes = 60,
        RefreshTokenDays = 7
    };

    private static (AccessTokenIssuer Issuer, FakeTimeProvider Time) Create()
    {
        var time = new FakeTimeProvider(Now);

        return (new AccessTokenIssuer(Microsoft.Extensions.Options.Options.Create(Options), time), time);
    }

    private static JwtSecurityToken Decode(string token) => new JwtSecurityTokenHandler().ReadJwtToken(token);

    [Fact]
    public void A_token_carries_the_user_id_facility_and_role()
    {
        var (issuer, _) = Create();

        var tokens = issuer.Issue(new TokenSubject("user-1", "k.perera", "MCC-KANDY", WonrichRoles.MccManager));
        var jwt = Decode(tokens.AccessToken);

        Assert.Equal("user-1", jwt.Claims.First(claim => claim.Type == ClaimTypes.NameIdentifier).Value);
        Assert.Equal("k.perera", jwt.Claims.First(claim => claim.Type == ClaimTypes.Name).Value);
        Assert.Equal("MCC-KANDY", jwt.Claims.First(claim => claim.Type == WonrichClaims.Facility).Value);
        Assert.Equal(WonrichRoles.MccManager, jwt.Claims.First(claim => claim.Type == ClaimTypes.Role).Value);
    }

    [Fact]
    public void A_token_is_stamped_with_the_configured_issuer_and_audience()
    {
        var (issuer, _) = Create();

        var jwt = Decode(issuer
            .Issue(new TokenSubject("user-1", "k.perera", null, WonrichRoles.IntakeOfficer))
            .AccessToken);

        Assert.Equal(Options.Issuer, jwt.Issuer);
        Assert.Contains(Options.Audience, jwt.Audiences);
    }

    [Fact]
    public void A_token_expires_after_the_configured_lifetime()
    {
        var (issuer, _) = Create();

        var tokens = issuer.Issue(new TokenSubject("user-1", "k.perera", null, WonrichRoles.IntakeOfficer));

        Assert.Equal(Now.UtcDateTime.AddMinutes(60), tokens.ExpiresAtUtc);
        Assert.Equal(Now.UtcDateTime.AddDays(7), tokens.RefreshExpiresAtUtc);
    }

    [Fact]
    public void A_facility_is_omitted_when_the_user_has_none()
    {
        var (issuer, _) = Create();

        var jwt = Decode(issuer
            .Issue(new TokenSubject("user-1", "admin", null, WonrichRoles.SystemAdministrator))
            .AccessToken);

        Assert.DoesNotContain(jwt.Claims, claim => claim.Type == WonrichClaims.Facility);
    }

    [Fact]
    public void Two_tokens_are_never_identical()
    {
        var (issuer, _) = Create();
        var subject = new TokenSubject("user-1", "k.perera", null, WonrichRoles.IntakeOfficer);

        // The jti claim keeps otherwise-identical tokens distinguishable in logs.
        Assert.NotEqual(issuer.Issue(subject).AccessToken, issuer.Issue(subject).AccessToken);
        Assert.NotEqual(issuer.Issue(subject).RefreshToken, issuer.Issue(subject).RefreshToken);
    }

    [Fact]
    public void A_role_outside_the_configured_seven_is_refused()
    {
        var (issuer, _) = Create();

        // Refusing here keeps a role no service has a policy for out of a signed token.
        Assert.Throws<InvalidOperationException>(
            () => issuer.Issue(new TokenSubject("user-1", "k.perera", null, "Chief Cheese Taster")));
    }

    [Theory]
    [InlineData(WonrichRoles.SystemAdministrator)]
    [InlineData(WonrichRoles.MccManager)]
    [InlineData(WonrichRoles.IntakeOfficer)]
    [InlineData(WonrichRoles.QualityAnalyst)]
    [InlineData(WonrichRoles.BowserOperator)]
    [InlineData(WonrichRoles.FactoryIntakeOfficer)]
    [InlineData(WonrichRoles.ProductionManager)]
    public void Every_configured_role_can_be_issued_a_token(string role)
    {
        var (issuer, _) = Create();

        Assert.NotEmpty(issuer.Issue(new TokenSubject("user-1", "user", null, role)).AccessToken);
    }

    [Fact]
    public void All_seven_roles_are_configured()
    {
        Assert.Equal(7, WonrichRoles.All.Count);
        Assert.False(WonrichRoles.IsConfigured("NotARole"));
        Assert.False(WonrichRoles.IsConfigured(null));
    }
}
