using System.Diagnostics;
using System.Globalization;
using Microsoft.AspNetCore.Routing;

namespace MccIntakeService.Api.Infrastructure.Observability;

/// <summary>
/// Records request count, duration and server errors for Prometheus. Runs after
/// CorrelationIdMiddleware and outside the exception handler, so a request that throws is still
/// counted with the 500 the handler wrote.
/// </summary>
public sealed class RequestMetricsMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        // Prometheus scrapes /metrics every 15 s; counting those would make the scraper the
        // service's busiest client and drown the real request rate.
        if (context.Request.Path.StartsWithSegments("/metrics"))
        {
            await next(context);
            return;
        }

        var started = Stopwatch.GetTimestamp();
        var status = StatusCodes.Status500InternalServerError; // stays 500 if the pipeline throws
        try
        {
            await next(context);
            status = context.Response.StatusCode;
        }
        finally
        {
            var elapsed = Stopwatch.GetElapsedTime(started);
            var method = context.Request.Method;
            var endpoint = EndpointLabel(context);
            var code = status.ToString(CultureInfo.InvariantCulture);

            MccMetrics.HttpRequests.WithLabels(method, endpoint, code).Inc();
            MccMetrics.HttpRequestDuration.WithLabels(method, endpoint, code).Observe(elapsed.TotalSeconds);
            if (status >= 500)
                MccMetrics.HttpRequestErrors.WithLabels(method, endpoint, code).Inc();
        }
    }

    /// <summary>
    /// The route template (e.g. "/api/consignments/{id}"), never the raw path: raw paths would create
    /// a new time series for every consignment ID, and requests matching no route share one
    /// "unmatched" label so scanners probing random URLs cannot do the same.
    /// </summary>
    private static string EndpointLabel(HttpContext context) =>
        context.GetEndpoint() is RouteEndpoint route
            ? "/" + (route.RoutePattern.RawText ?? "").TrimStart('/')
            : "unmatched";
}

public static class RequestMetricsMiddlewareExtensions
{
    public static IApplicationBuilder UseRequestMetrics(this IApplicationBuilder app) =>
        app.UseMiddleware<RequestMetricsMiddleware>();
}
