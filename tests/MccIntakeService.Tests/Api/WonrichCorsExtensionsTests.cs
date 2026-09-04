using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Wonrich.Auth;

namespace MccIntakeService.Tests.Api;

/// <summary>
/// What the shared CORS policy is built from (SCRUM-92). Exercised directly rather than through a
/// host, so the answer does not depend on which appsettings file an environment happens to layer
/// on top.
/// </summary>
public class WonrichCorsExtensionsTests
{
    private static CorsPolicy PolicyFrom(params (string Key, string? Value)[] settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(pair =>
                new KeyValuePair<string, string?>(pair.Key, pair.Value)))
            .Build();

        var provider = new ServiceCollection()
            .AddWonrichCors(configuration)
            .BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<CorsOptions>>().Value;

        return options.GetPolicy(WonrichCorsExtensions.PolicyName)!;
    }

    [Fact]
    public void The_configured_origins_are_the_ones_allowed()
    {
        var policy = PolicyFrom(
            ("Cors:AllowedOrigins:0", "https://mcc.wonrich.example"),
            ("Cors:AllowedOrigins:1", "http://localhost:5173"));

        Assert.Equal(
            ["https://mcc.wonrich.example", "http://localhost:5173"],
            policy.Origins);
        Assert.False(policy.AllowAnyOrigin);
    }

    [Fact]
    public void An_environment_told_of_no_origin_allows_none()
    {
        // The safe default. A service that has not been told about its client must not fall back
        // to allowing anything.
        var policy = PolicyFrom();

        Assert.Empty(policy.Origins);
        Assert.False(policy.AllowAnyOrigin);
    }

    [Fact]
    public void A_blank_entry_is_dropped_rather_than_registered()
    {
        // An environment variable set to an empty string would otherwise become an origin no
        // browser can match, hiding the fact that nothing was configured.
        var policy = PolicyFrom(
            ("Cors:AllowedOrigins:0", ""),
            ("Cors:AllowedOrigins:1", "   "),
            ("Cors:AllowedOrigins:2", "https://mcc.wonrich.example"));

        Assert.Equal(["https://mcc.wonrich.example"], policy.Origins);
    }

    [Fact]
    public void Credentials_are_not_allowed()
    {
        // The token travels in the Authorization header, not a cookie. Nothing needs credentials,
        // and allowing them widens what a hostile page can attempt.
        var policy = PolicyFrom(("Cors:AllowedOrigins:0", "http://localhost:5173"));

        Assert.False(policy.SupportsCredentials);
    }
}
