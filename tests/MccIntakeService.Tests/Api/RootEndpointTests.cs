using System.Net;
using MccIntakeService.Tests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;

namespace MccIntakeService.Tests.Api;

/// <summary>
/// The site root. Opening the App Service address in a browser answered a bare 404, so a healthy
/// deployment looked like a failed one.
/// </summary>
public class RootEndpointTests
{
    [Fact]
    public async Task Outside_production_the_root_opens_the_swagger_ui()
    {
        using var factory = new IntakeApiFactory();
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/swagger", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task The_root_needs_no_token()
    {
        using var factory = new IntakeApiFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode); // followed through to the Swagger UI
    }

    [Fact]
    public async Task In_production_the_root_answers_a_status_document()
    {
        using var factory = new IntakeApiFactory();
        using var production = factory.WithWebHostBuilder(b => b.UseEnvironment(Environments.Production));
        var client = production.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"status\":\"running\"", await response.Content.ReadAsStringAsync());
    }
}
