using MccIntakeService.Api.Contracts;
using MccIntakeService.Api.Infrastructure;
using MccIntakeService.Application.Traceability;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MccIntakeService.Controllers;

/// <summary>
/// Resolves a production batch back through the dispatch note and tanks to the society
/// consignments that made it up (SCRUM-12).
/// </summary>
/// <remarks>
/// <para>
/// This sits on its own controller rather than alongside factory intake because it answers to a
/// different policy. Stacked <c>[Authorize]</c> attributes are combined, not replaced, so a trace
/// action nested under the factory controller would have demanded both policies and shut out the
/// quality analysts the story is written for.
/// </para>
/// <para>
/// SCRUM-12 calls for service-to-service authentication. With one shared token scheme that means
/// a token issued to a service account holding a role in
/// <see cref="IntakePolicies.TraceBatches"/>, rather than a separate mechanism.
/// </para>
/// </remarks>
[ApiController]
[Route("api/factory/batches")]
[Authorize(Policy = IntakePolicies.TraceBatches)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
public class BatchTraceController : ControllerBase
{
    private const string ProblemJson = "application/problem+json";

    private readonly IBatchTraceService _trace;

    public BatchTraceController(IBatchTraceService trace)
    {
        _trace = trace;
    }

    /// <summary>
    /// Resolves a batch to the dispatch note, the tanks it drew from and every consignment in
    /// those tanks, with the full gate results for each.
    /// </summary>
    /// <remarks>
    /// Contributing societies are ranked most marginal first — the supplier whose milk passed the
    /// gate by the narrowest room comes first, so a QCO chasing a bad batch has somewhere to
    /// start. Anything that could not be resolved upstream is listed explicitly rather than left
    /// blank, so a gap never reads as a clean result.
    /// </remarks>
    /// <response code="200">The resolved batch.</response>
    /// <response code="404">No batch carries that reference.</response>
    [HttpGet("{reference}/trace")]
    [ProducesResponseType(typeof(BatchTraceView), StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType(typeof(IntakeProblemDetails), StatusCodes.Status404NotFound, ProblemJson)]
    public async Task<ActionResult<BatchTraceView>> Trace(string reference, CancellationToken cancellationToken)
    {
        var trace = await _trace.TraceAsync(reference, cancellationToken);

        if (trace is null)
        {
            return this.IntakeProblem(
                StatusCodes.Status404NotFound,
                "entity_not_found",
                "Batch not found",
                $"No batch carries the reference '{reference}'.");
        }

        return Ok(trace);
    }
}
