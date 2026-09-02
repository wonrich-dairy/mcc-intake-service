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

        if (domainException is DuplicateCodeException duplicateCodeException)
        {
            problemDetails.Extensions["conflictingCode"] = duplicateCodeException.ConflictingCode;
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

        // A code the caller chose collides with one already in use.
        DuplicateCodeException => (StatusCodes.Status409Conflict, "Code already in use"),

        // The milk is already in a tank: a conflict with what has been recorded, not bad input.
        ConsignmentAlreadyPouredException => (StatusCodes.Status409Conflict, "Consignment already poured"),

        // Likewise a second gate verdict or a second screening of the same bowser: the record
        // already answers the question, so these are conflicts rather than bad input.
        ConsignmentAlreadyTestedException => (StatusCodes.Status409Conflict, "Consignment already tested"),
        ArrivalAlreadyScreenedException => (StatusCodes.Status409Conflict, "Arrival already screened"),

        // Something the request body points at does not exist. A resource addressed by the route
        // instead answers 404, which the controllers handle themselves.
        EntityNotFoundException => (StatusCodes.Status422UnprocessableEntity, "Referenced record does not exist"),

        _ => (StatusCodes.Status400BadRequest, "Request could not be completed")
    };
}
