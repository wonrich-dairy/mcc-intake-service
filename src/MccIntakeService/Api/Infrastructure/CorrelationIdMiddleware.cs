using System.Text.RegularExpressions;

namespace MccIntakeService.Api.Infrastructure;

/// <summary>
/// Correlation ID for every request (SCRUM-132), the same contract as Traceability and Quality Lab.
///
/// Reuses the caller's X-Correlation-ID when it is present and well-formed, otherwise generates one.
/// The ID goes back in the response header and into the logging scope, so every log line written
/// while handling the request carries "CorrelationId" and one ID can be followed across the hop in Loki.
/// </summary>
public sealed partial class CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
{
    public const string HeaderName = "X-Correlation-ID";

    public async Task InvokeAsync(HttpContext context)
    {
        var incoming = context.Request.Headers[HeaderName].FirstOrDefault();

        // Anything else is replaced rather than logged: a header value ends up verbatim in every log
        // line, so accepting arbitrary text would let a caller forge or break log entries.
        var correlationId = incoming is not null && SafeId().IsMatch(incoming)
            ? incoming
            : Guid.NewGuid().ToString("N");

        context.Items[HeaderName] = correlationId;

        // Set as the response starts rather than now: the exception handler clears the response
        // headers before writing its 500, and that is the response a caller most needs the ID on.
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
        {
            await next(context);
        }
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{1,64}$")]
    private static partial Regex SafeId();
}

public static class CorrelationIdMiddlewareExtensions
{
    public static IApplicationBuilder UseCorrelationId(this IApplicationBuilder app) =>
        app.UseMiddleware<CorrelationIdMiddleware>();
}
