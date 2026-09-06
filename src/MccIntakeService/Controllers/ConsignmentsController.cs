using MccIntakeService.Api.Contracts;
using MccIntakeService.Api.Infrastructure;
using MccIntakeService.Application.Consignments;
using MccIntakeService.Domain.Consignments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MccIntakeService.Controllers;

/// <summary>
/// Registration and lookup of society consignments arriving at the milk chilling centre (SCRUM-6).
/// </summary>
/// <remarks>
/// Every action needs an authenticated caller. A consignment carries supplier names, contact
/// numbers, per-can quantities and the officer who registered it, so the lookups are no more
/// public than the registration is. Stacked <c>[Authorize]</c> attributes are combined rather than
/// replaced, so <see cref="IntakePolicies.RegisterConsignments"/> still narrows the write.
/// </remarks>
[ApiController]
[Route("api/consignments")]
[Authorize]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
public class ConsignmentsController : ControllerBase
{
    /// <summary>Media type every error response on this controller is served as (RFC 9457).</summary>
    private const string ProblemJson = "application/problem+json";

    private readonly IConsignmentService _consignments;

    public ConsignmentsController(IConsignmentService consignments)
    {
        _consignments = consignments;
    }

    /// <summary>Registers an arriving society consignment.</summary>
    /// <remarks>
    /// The total quantity is derived from the can entries and the reference is allocated by the
    /// service as MCC-YYYYMMDD-SOCIETY-NN. Consignments arriving after the configured daily cutoff
    /// are rejected with 422 and a message naming the cutoff.
    /// </remarks>
    /// <response code="201">The consignment was registered.</response>
    /// <response code="400">
    /// The submitted can sheet is incomplete or invalid — no cans, a non-positive quantity, the
    /// same can entered twice, or an arrival time in the future.
    /// </response>
    /// <response code="422">
    /// Either the society is not registered (<c>code: entity_not_found</c>) or intake has closed
    /// for the day (<c>code: intake_cutoff_exceeded</c>, which also carries <c>cutoff</c> and
    /// <c>arrivalTime</c>). Branch on <c>code</c> to tell them apart.
    /// </response>
    [HttpPost]
    [Authorize(Policy = IntakePolicies.RegisterConsignments)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ConsignmentView), StatusCodes.Status201Created, "application/json")]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest, ProblemJson)]
    [ProducesResponseType(typeof(IntakeUnprocessableProblemDetails), StatusCodes.Status422UnprocessableEntity, ProblemJson)]
    public async Task<ActionResult<ConsignmentView>> Register(
        [FromBody] RegisterConsignmentRequest request,
        CancellationToken cancellationToken)
    {
        var command = new RegisterConsignmentCommand(
            request.SocietyId,
            request.ToCanEntries(),
            request.ArrivalAtLocal,
            User.OfficerIdentity());

        var consignment = await _consignments.RegisterAsync(command, cancellationToken);

        return CreatedAtAction(
            nameof(GetByReference),
            new { reference = consignment.Reference },
            consignment);
    }

    /// <summary>Fetches a single consignment by its reference.</summary>
    /// <param name="reference">Consignment reference, e.g. MCC-20260823-KC-01.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="200">The consignment was found.</response>
    /// <response code="404">No consignment carries that reference.</response>
    [HttpGet("{reference}")]
    [ProducesResponseType(typeof(ConsignmentView), StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType(typeof(IntakeProblemDetails), StatusCodes.Status404NotFound, ProblemJson)]
    public async Task<ActionResult<ConsignmentView>> GetByReference(
        string reference,
        CancellationToken cancellationToken)
    {
        var consignment = await _consignments.GetByReferenceAsync(reference, cancellationToken);

        if (consignment is null)
        {
            return this.IntakeProblem(
                StatusCodes.Status404NotFound,
                "entity_not_found",
                "Consignment not found",
                $"No consignment is registered under reference '{reference}'.");
        }

        return Ok(consignment);
    }

    /// <summary>Lists registered consignments filtered by society, date range or reference.</summary>
    /// <param name="societyId">Filter to one society by identifier.</param>
    /// <param name="societyCode">Filter to one society by its short code, e.g. KC.</param>
    /// <param name="reference">Filter to an exact consignment reference.</param>
    /// <param name="date">Filter to a single intake date.</param>
    /// <param name="from">Start of an intake date range, inclusive.</param>
    /// <param name="to">End of an intake date range, inclusive.</param>
    /// <param name="status">Restrict to one lifecycle state: Registered, Accepted or Rejected.</param>
    /// <param name="page">1-based page number.</param>
    /// <param name="pageSize">Page size, capped at 200.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="200">A page of matching consignments.</response>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<ConsignmentView>), StatusCodes.Status200OK, "application/json")]
    public async Task<ActionResult<PagedResult<ConsignmentView>>> Search(
        [FromQuery] Guid? societyId,
        [FromQuery] string? societyCode,
        [FromQuery] string? reference,
        [FromQuery] DateOnly? date,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] ConsignmentStatus? status,
        CancellationToken cancellationToken,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25)
    {
        var query = new ConsignmentQuery
        {
            SocietyId = societyId,
            SocietyCode = societyCode,
            Reference = reference,
            ArrivalDate = date,
            FromDate = from,
            ToDate = to,
            Status = status,
            Page = page,
            PageSize = pageSize
        };

        return Ok(await _consignments.SearchAsync(query, cancellationToken));
    }
}
