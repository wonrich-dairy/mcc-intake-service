using System.Net;
using MccIntakeService.Tests.Support;
using Microsoft.AspNetCore.Mvc.Testing;

namespace MccIntakeService.Tests.Api;

/// <summary>
/// GET /metrics. Prometheus scrapes it anonymously, and before it existed the scrape answered 404, so
/// MCC showed as down in Grafana with no request, error or latency data.
/// </summary>
public class MetricsEndpointTests
{
    [Fact]
    public async Task Metrics_is_served_anonymously()
    {
        using var factory = new IntakeApiFactory();

        var response = await factory.CreateClient().GetAsync("/metrics");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Request_metrics_are_labelled_by_route_template()
    {
        using var factory = new IntakeApiFactory();
        var client = factory.CreateClient();
        await client.GetAsync("/api/factory/batches/WR-20260823-01/trace");

        var body = await client.GetStringAsync("/metrics");

        Assert.Contains("mcc_http_requests_total{", body);
        Assert.Contains("mcc_http_request_duration_seconds_bucket{", body);
        Assert.Contains("endpoint=\"/api/factory/batches/{reference}/trace\"", body);
        Assert.DoesNotContain("WR-20260823-01", body);
    }

    [Fact]
    public async Task Scrapes_of_metrics_are_not_counted_as_traffic()
    {
        using var factory = new IntakeApiFactory();
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await client.GetAsync("/metrics");

        var body = await client.GetStringAsync("/metrics");

        Assert.DoesNotContain("endpoint=\"/metrics\"", body);
    }
}
