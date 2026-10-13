using System.Net;
using MccIntakeService.Api.Infrastructure;
using MccIntakeService.Application.Traceability;
using MccIntakeService.Tests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;
using Wonrich.Auth.Authorization;

namespace MccIntakeService.Tests.Api;

/// <summary>
/// The correlation ID (SCRUM-132). Traceability sends X-Correlation-ID on every call, and its DoD
/// is one ID followed across the HTTP hop in Loki, which needs it on this side's log lines too.
/// </summary>
public class CorrelationIdTests
{
    private const string Header = CorrelationIdMiddleware.HeaderName;

    [Fact]
    public async Task A_well_formed_incoming_id_is_echoed_back()
    {
        using var factory = new IntakeApiFactory();
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add(Header, "trace-01_ABC");

        var response = await client.SendAsync(request);

        Assert.Equal("trace-01_ABC", Assert.Single(response.Headers.GetValues(Header)));
    }

    [Theory]
    [InlineData("has space")]
    [InlineData("semi;colon")]
    [InlineData("0123456789012345678901234567890123456789012345678901234567890123X")]
    public async Task A_malformed_incoming_id_is_replaced_not_reused(string incoming)
    {
        using var factory = new IntakeApiFactory();
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.TryAddWithoutValidation(Header, incoming);

        var response = await client.SendAsync(request);

        var echoed = Assert.Single(response.Headers.GetValues(Header));
        Assert.NotEqual(incoming, echoed);
        Assert.Matches("^[0-9a-f]{32}$", echoed);
    }

    [Fact]
    public async Task An_id_is_generated_when_none_is_sent()
    {
        using var factory = new IntakeApiFactory();
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/");

        Assert.Matches("^[0-9a-f]{32}$", Assert.Single(response.Headers.GetValues(Header)));
    }

    [Fact]
    public async Task A_refused_request_still_carries_the_id()
    {
        using var factory = new IntakeApiFactory();
        var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/factory/batches/WR-20260823-01/trace");
        request.Headers.Add(Header, "refused-call");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("refused-call", Assert.Single(response.Headers.GetValues(Header)));
    }

    [Fact]
    public async Task A_500_still_carries_the_id()
    {
        // The exception handler clears the response headers before writing its problem document,
        // which would drop a header set up front — on exactly the response worth chasing in Loki.
        using var intake = new IntakeApiFactory();
        using var factory = intake.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddScoped<IBatchTraceService, ThrowingTraceService>()));

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", IntakeApiFactory.IssueTokenFor(WonrichRoles.QualityAnalyst));

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/factory/batches/WR-20260823-01/trace");
        request.Headers.Add(Header, "failed-call");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("failed-call", Assert.Single(response.Headers.GetValues(Header)));
    }

    [Fact]
    public async Task Log_lines_written_while_handling_the_request_carry_the_id()
    {
        var logs = new ScopeCapturingLoggerProvider();

        using var intake = new IntakeApiFactory();
        using var factory = intake.WithWebHostBuilder(builder => builder.ConfigureLogging(logging =>
        {
            logging.AddProvider(logs);
            logging.AddFilter<ScopeCapturingLoggerProvider>(null, LogLevel.Trace);
        }));

        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add(Header, "logged-call");

        await client.SendAsync(request);

        Assert.Contains(
            logs.Entries,
            entry => entry.Category.StartsWith("Microsoft.AspNetCore.Routing", StringComparison.Ordinal)
                && Equals(entry.Scope.GetValueOrDefault("CorrelationId"), "logged-call"));
    }

    [Fact]
    public void The_console_writes_json_with_scopes_so_Loki_sees_the_id()
    {
        using var factory = new IntakeApiFactory();

        var console = factory.Services.GetRequiredService<IOptionsMonitor<ConsoleLoggerOptions>>().CurrentValue;
        var json = factory.Services.GetRequiredService<IOptionsMonitor<JsonConsoleFormatterOptions>>().CurrentValue;

        Assert.Equal(ConsoleFormatterNames.Json, console.FormatterName);
        Assert.True(json.IncludeScopes);
    }

    private sealed class ThrowingTraceService : IBatchTraceService
    {
        public Task<BatchTraceView?> TraceAsync(string batchReference, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Simulated failure.");
    }
}
