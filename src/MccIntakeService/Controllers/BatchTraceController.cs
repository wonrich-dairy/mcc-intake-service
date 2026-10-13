using MccIntakeService.Api.Contracts;
using MccIntakeService.Api.Infrastructure;
using MccIntakeService.Application.Factory;
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
    private readonly IFactoryIntakeService _factory;

    public BatchTraceController(IBatchTraceService trace, IFactoryIntakeService factory)
    {
        _trace = trace;
        _factory = factory;
    }

    /// <summary>Finds the batch made from one dispatch note, so a caller holding only the note can trace it.</summary>
    /// <remarks>
    /// A read-only copy of <c>GET /api/factory/batches?dispatchNote=</c> under the trace policy
    /// (SCRUM-132). The Traceability service knows a batch only by its dispatch note, and this lets
    /// its service account resolve the reference without holding factory intake permissions. The
    /// response has the list endpoint's shape: empty for an unknown or unscreened note, newest first.
    /// </remarks>
    /// <response code="200">The batches made from that note — at most one, as a note is screened once.</response>
    [HttpGet("by-dispatch-note/{dispatchNoteReference}")]
    [ProducesResponseType(typeof(IReadOnlyList<BatchView>), StatusCodes.Status200OK, "application/json")]
    public async Task<ActionResult<IReadOnlyList<BatchView>>> FindByDispatchNote(
        string dispatchNoteReference,
        CancellationToken cancellationToken) =>
        Ok(await _factory.ListBatchesAsync(dispatchNoteReference: dispatchNoteReference, cancellationToken: cancellationToken));

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
