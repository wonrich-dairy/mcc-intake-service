using Prometheus;

namespace MccIntakeService.Api.Infrastructure.Observability;

/// <summary>
/// Prometheus metrics for the MCC &amp; Intake Service, served on GET /metrics.
///
/// Request metrics follow the Wonrich convention &lt;service&gt;_http_* with the labels method, endpoint and
/// status, the same as Processing, Quality Lab and Traceability, so the shared dashboard and the
/// HighErrorRate alert pick the service up by metric-name pattern.
/// The service and environment labels are added by Prometheus from the scrape job, not here.
/// </summary>
public static class MccMetrics
{
    private static readonly string[] RequestLabels = { "method", "endpoint", "status" };

    public static readonly Counter HttpRequests = Metrics.CreateCounter(
        "mcc_http_requests_total",
        "HTTP requests handled, by endpoint and status code.",
        new CounterConfiguration { LabelNames = RequestLabels });

    /// <summary>Server errors (5xx) only. A 4xx is the caller's mistake, not the service failing.</summary>
    public static readonly Counter HttpRequestErrors = Metrics.CreateCounter(
        "mcc_http_request_errors_total",
        "HTTP requests that ended in a server error (5xx), by endpoint.",
        new CounterConfiguration { LabelNames = RequestLabels });

    public static readonly Histogram HttpRequestDuration = Metrics.CreateHistogram(
        "mcc_http_request_duration_seconds",
        "HTTP request duration in seconds, by endpoint and status code.",
        new HistogramConfiguration
        {
            LabelNames = RequestLabels,
            Buckets = Histogram.ExponentialBuckets(start: 0.005, factor: 2, count: 12)
        });
}
