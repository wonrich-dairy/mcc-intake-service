using MccIntakeService.Api.Contracts;
using MccIntakeService.Application.Consignments;
using Microsoft.AspNetCore.Mvc;

namespace MccIntakeService.Controllers;

/// <summary>
/// Registration and lookup of society consignments arriving at the milk chilling centre (SCRUM-6).
/// </summary>
[ApiController]
[Route("api/consignments")]
[Produces("application/json")]
public class ConsignmentsController : ControllerBase
{
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
    /// <response code="400">The submitted can sheet is incomplete or invalid.</response>
    /// <response code="422">The society does not exist, or intake has closed for the day.</response>
    [HttpPost]
    [ProducesResponseType(typeof(ConsignmentView), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ConsignmentView>> Register(
        [FromBody] RegisterConsignmentRequest request,
        CancellationToken cancellationToken)
    {
        var command = new RegisterConsignmentCommand(
            request.SocietyId,
            request.ToCanEntries(),
            request.ArrivalAtLocal);

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
    [ProducesResponseType(typeof(ConsignmentView), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ConsignmentView>> GetByReference(
        string reference,
        CancellationToken cancellationToken)
    {
        var consignment = await _consignments.GetByReferenceAsync(reference, cancellationToken);

        if (consignment is null)
        {
            return Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Consignment not found",
                detail: $"No consignment is registered under reference '{reference}'.");
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
    /// <param name="page">1-based page number.</param>
    /// <param name="pageSize">Page size, capped at 200.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="200">A page of matching consignments.</response>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<ConsignmentView>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<ConsignmentView>>> Search(
        [FromQuery] Guid? societyId,
        [FromQuery] string? societyCode,
        [FromQuery] string? reference,
        [FromQuery] DateOnly? date,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
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
            Page = page,
            PageSize = pageSize
        };

        return Ok(await _consignments.SearchAsync(query, cancellationToken));
    }
}
