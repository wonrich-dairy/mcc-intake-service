using MccIntakeService.Domain.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace MccIntakeService.Api.Infrastructure;

/// <summary>
/// Translates domain rule violations into ProblemDetails responses, so controllers stay free of
/// try/catch and every rule failure reaches the officer as a readable message.
/// </summary>
public sealed class DomainExceptionHandler : IExceptionHandler
{
    private readonly IProblemDetailsService _problemDetailsService;
    private readonly ILogger<DomainExceptionHandler> _logger;

    public DomainExceptionHandler(
        IProblemDetailsService problemDetailsService,
        ILogger<DomainExceptionHandler> logger)
    {
        _problemDetailsService = problemDetailsService;
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not DomainException domainException)
        {
            return false;
        }

        var (statusCode, title) = Describe(domainException);

        _logger.LogInformation(
            "Rejected {Method} {Path}: {Code} - {Message}",
            httpContext.Request.Method,
            httpContext.Request.Path,
            domainException.Code,
            domainException.Message);

        var problemDetails = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = domainException.Message,
            Type = $"https://wonrich.dev/problems/{domainException.Code}"
        };

        problemDetails.Extensions["code"] = domainException.Code;

        if (domainException is IntakeCutoffExceededException cutoffException)
        {
            problemDetails.Extensions["cutoff"] = cutoffException.Cutoff.ToString("HH:mm");
            problemDetails.Extensions["arrivalTime"] = cutoffException.ArrivalTimeOfDay.ToString("HH:mm");
        }

        httpContext.Response.StatusCode = statusCode;

        return await _problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = problemDetails
        });
    }

    private static (int StatusCode, string Title) Describe(DomainException exception) => exception switch
    {
        // The request is well formed but breaks a rule of the intake process.
        IntakeCutoffExceededException => (StatusCodes.Status422UnprocessableEntity, "Intake closed for the day"),
        EntityNotFoundException => (StatusCodes.Status422UnprocessableEntity, "Referenced record does not exist"),
        _ => (StatusCodes.Status400BadRequest, "Consignment could not be registered")
    };
}
