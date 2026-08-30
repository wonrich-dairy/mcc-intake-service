using MccIntakeService.Api.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace MccIntakeService.Api.Infrastructure;

/// <summary>
/// Builds the refusals a controller writes itself, in the same shape
/// <see cref="DomainExceptionHandler"/> writes the ones it handles.
/// </summary>
/// <remarks>
/// <see cref="ControllerBase.Problem(string, string, int?, string, string)"/> alone produces a bare
/// <see cref="ProblemDetails"/>: no <c>code</c>, no type URI. Endpoints documenting
/// <see cref="IntakeProblemDetails"/> would then answer one refusal a consumer can branch on and
/// another it cannot, from the same route.
/// </remarks>
public static class IntakeProblemResults
{
    /// <summary>Writes a refusal carrying the machine-readable code consumers branch on.</summary>
    /// <param name="controller">The controller writing the response.</param>
    /// <param name="statusCode">HTTP status for the refusal.</param>
    /// <param name="code">Stable identifier for the rule that was broken.</param>
    /// <param name="title">Short human-readable summary.</param>
    /// <param name="detail">Prose explaining this particular refusal.</param>
    public static ObjectResult IntakeProblem(
        this ControllerBase controller,
        int statusCode,
        string code,
        string title,
        string detail)
    {
        // Problem(...) is what supplies traceId, so the body is built through it rather than by
        // hand, and only the two members it cannot know are added afterwards.
        var result = controller.Problem(
            statusCode: statusCode,
            title: title,
            detail: detail,
            type: $"https://wonrich.dev/problems/{code}");

        if (result.Value is ProblemDetails problem)
        {
            problem.Extensions["code"] = code;
        }

        return result;
    }
}
