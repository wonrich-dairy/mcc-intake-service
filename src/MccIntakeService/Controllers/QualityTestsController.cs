using System.ComponentModel.DataAnnotations;
using MccIntakeService.Api.Contracts;
using MccIntakeService.Api.Infrastructure;
using MccIntakeService.Application.QualityTests;
using MccIntakeService.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wonrich.Auth.Tokens;

namespace MccIntakeService.Controllers;

/// <summary>
/// The quality test panel recorded for a consignment's composite sample at the gate (SCRUM-7).
/// </summary>
/// <remarks>
/// A consignment is tested once, and the record never changes afterwards: it is the evidence
/// behind accepting or rejecting a delivery the society is paid for.
/// </remarks>
[ApiController]
[Route("api/consignments/{reference}/quality-test")]
[Authorize(Policy = IntakePolicies.RecordQualityTests)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
public class QualityTestsController : ControllerBase
{
    private const string ProblemJson = "application/problem+json";

    private readonly IQualityTestService _tests;

    public QualityTestsController(IQualityTestService tests)
    {
        _tests = tests;
    }

    /// <summary>
    /// Evaluates readings without recording anything, so the officer can see the corrected
    /// lactometer reading, SNF and TS, and anything outside its limit, before submitting.
    /// </summary>
    /// <response code="200">The derived values and any breaches.</response>
    /// <response code="400">The readings are incomplete or invalid.</response>
    [HttpPost("preview")]
    [ProducesResponseType(typeof(TestPreview), StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest, ProblemJson)]
    public ActionResult<TestPreview> Preview([FromBody] RecordQualityTestRequest request)
    {
        // Verdict is irrelevant to a preview; the officer has not decided one yet.
        return Ok(_tests.Preview(request.ToCommand(Domain.QualityTests.TestVerdict.Accept, null)));
    }

    /// <summary>Records the panel and settles the consignment's verdict.</summary>
    /// <remarks>
    /// A positive clot-on-boiling forces a rejection: the milk is already curdled, so acceptance
    /// is refused rather than left to judgement. A rejection must name the failed parameter and
    /// its recorded value.
    /// </remarks>
    /// <response code="201">The panel was recorded.</response>
    /// <response code="400">The readings are invalid, or a rejection is missing its reason.</response>
    /// <response code="404">No consignment carries that reference.</response>
    /// <response code="409">That consignment has already been tested.</response>
    [HttpPost]
    [ProducesResponseType(typeof(QualityTestView), StatusCodes.Status201Created, "application/json")]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest, ProblemJson)]
    [ProducesResponseType(typeof(IntakeProblemDetails), StatusCodes.Status404NotFound, ProblemJson)]
    [ProducesResponseType(typeof(IntakeProblemDetails), StatusCodes.Status409Conflict, ProblemJson)]
    public async Task<ActionResult<QualityTestView>> Record(
        string reference,
        [FromBody] RecordQualityTestRequest request,
        CancellationToken cancellationToken)
    {
        var command = request.ToCommand(request.Verdict, User.OfficerIdentity());

        try
        {
            var view = await _tests.RecordAsync(reference, command, cancellationToken);

            return CreatedAtAction(nameof(Get), new { reference }, view);
        }
        catch (EntityNotFoundException exception)
        {
            // Addressed by the route, so 404 rather than the 422 the handler gives a body
            // reference. The prose names the reference the caller used; the code comes from the
            // exception so it cannot drift from what the handler writes.
            return this.IntakeProblem(
                StatusCodes.Status404NotFound,
                exception.Code,
                "Consignment not found",
                $"No consignment is registered under reference '{reference}'.");
        }
        catch (DomainValidationException exception) when (exception.Message.Contains(
            "already been tested", StringComparison.OrdinalIgnoreCase))
        {
            // Through the helper, not Problem(...), so the refusal carries the code this route
            // publishes and its 404 already writes. The code comes from the exception so it cannot
            // drift from what the handler writes for the same rule.
            return this.IntakeProblem(
                StatusCodes.Status409Conflict,
                exception.Code,
                "Consignment already tested",
                exception.Message);
        }
    }

    /// <summary>Reads back the panel recorded against a consignment.</summary>
    /// <response code="200">The recorded panel.</response>
    /// <response code="404">That consignment has not been tested.</response>
    [HttpGet]
    [ProducesResponseType(typeof(QualityTestView), StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType(typeof(IntakeProblemDetails), StatusCodes.Status404NotFound, ProblemJson)]
    public async Task<ActionResult<QualityTestView>> Get(string reference, CancellationToken cancellationToken)
    {
        var view = await _tests.GetForConsignmentAsync(reference, cancellationToken);

        if (view is null)
        {
            return this.IntakeProblem(
                StatusCodes.Status404NotFound,
                "entity_not_found",
                "No quality test recorded",
                $"Consignment '{reference}' has not been tested.");
        }

        return Ok(view);
    }
}
