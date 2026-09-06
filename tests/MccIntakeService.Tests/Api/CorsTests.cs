using System.Net;
using MccIntakeService.Tests.Support;
using Wonrich.Auth;

namespace MccIntakeService.Tests.Api;

/// <summary>
/// The browser origin policy (SCRUM-92).
/// </summary>
/// <remarks>
/// <para>
/// The service shipped without CORS in the pipeline and the SPA could not sign in at all. The
/// preflight was not short-circuited ahead of endpoint selection, so <c>OPTIONS</c> reached MVC,
/// which has no action for it, and answered 405. A browser reports that with no status and no
/// headers, so the client could not tell a misconfigured service from an unreachable one and
/// blamed the officer's network.
/// </para>
/// <para>
/// The fix landed without a test. These are that test: the failure is invisible to every other
/// kind of check, because the service answers a plain request perfectly well and only a browser
/// ever sends the preflight.
/// </para>
/// </remarks>
public class CorsTests
{
    [Fact]
    public async Task A_preflight_from_the_configured_origin_is_answered_with_its_headers()
    {
        using var factory = new IntakeApiFactory();
        var client = factory.CreateClient();

        using var preflight = new HttpRequestMessage(HttpMethod.Options, "/api/consignments");
        preflight.Headers.Add("Origin", IntakeApiFactory.AllowedTestOrigin);
        preflight.Headers.Add("Access-Control-Request-Method", "GET");
        preflight.Headers.Add("Access-Control-Request-Headers", "authorization");

        var response = await client.SendAsync(preflight);

        Assert.NotEqual(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.True(
            response.Headers.TryGetValues("Access-Control-Allow-Origin", out var allowed),
            "The preflight was answered without an Access-Control-Allow-Origin header, so the "
            + "browser will refuse the real request.");
        Assert.Equal(IntakeApiFactory.AllowedTestOrigin, Assert.Single(allowed));
    }

    [Fact]
    public async Task The_preflight_is_answered_before_the_request_is_authenticated()
    {
        using var factory = new IntakeApiFactory();

        // No Authorization header, as a real preflight carries none. Answering it must not depend
        // on authenticating it, which is why the middleware sits ahead of authentication.
        var client = factory.CreateClient();

        using var preflight = new HttpRequestMessage(HttpMethod.Options, "/api/tanks");
        preflight.Headers.Add("Origin", IntakeApiFactory.AllowedTestOrigin);
        preflight.Headers.Add("Access-Control-Request-Method", "POST");

        var response = await client.SendAsync(preflight);

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.True(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task An_origin_that_is_not_configured_is_not_allowed()
    {
        using var factory = new IntakeApiFactory();
        var client = factory.CreateClient();

        using var preflight = new HttpRequestMessage(HttpMethod.Options, "/api/consignments");
        preflight.Headers.Add("Origin", "https://not-the-wonrich-client.example");
        preflight.Headers.Add("Access-Control-Request-Method", "GET");

        var response = await client.SendAsync(preflight);

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public void The_policy_is_registered_and_applied_under_one_name()
    {
        // Registered and applied through the same extension, so a service cannot add the policy
        // under one name and apply another - which is how it came to be registered but never used.
        Assert.Equal("frontend", WonrichCorsExtensions.PolicyName);
    }
}
