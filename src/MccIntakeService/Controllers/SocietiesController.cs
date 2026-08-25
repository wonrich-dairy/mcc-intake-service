using MccIntakeService.Api.Contracts;
using MccIntakeService.Api.Infrastructure;
using MccIntakeService.Application.Societies;
using MccIntakeService.Domain.Common;
using Microsoft.AspNetCore.Mvc;

namespace MccIntakeService.Controllers;

/// <summary>
/// Registered supplying societies and their can labels (SCRUM-51). Intake officers read this
/// list to pick a society at the gate; MCC Managers maintain it.
/// </summary>
/// <remarks>
/// Societies are never deleted. Retiring one keeps it resolvable from historical consignments
/// while removing it from the list an officer can select at the gate.
/// </remarks>
[ApiController]
[Route("api/societies")]
public class SocietiesController : ControllerBase
{
    /// <summary>Media type every error response on this controller is served as (RFC 9457).</summary>
    private const string ProblemJson = "application/problem+json";

    private readonly ISocietyService _societies;

    public SocietiesController(ISocietyService societies)
    {
        _societies = societies;
    }

    /// <summary>Lists societies, searchable by name or code and sortable.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="search">Fragment matched against both society name and code.</param>
    /// <param name="includeInactive">Include retired societies alongside active ones.</param>
    /// <param name="sortBy">Field to order by: Code, Name or IsActive.</param>
    /// <param name="descending">Reverse the sort order.</param>
    /// <response code="200">The matching societies.</response>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<SocietyView>), StatusCodes.Status200OK, "application/json")]
    public async Task<ActionResult<IReadOnlyList<SocietyView>>> List(
        CancellationToken cancellationToken,
        [FromQuery] string? search = null,
        [FromQuery] bool includeInactive = false,
        [FromQuery] SocietySortBy sortBy = SocietySortBy.Code,
        [FromQuery] bool descending = false)
    {
        var query = new SocietyQuery
        {
            Search = search,
            ActiveOnly = !includeInactive,
            SortBy = sortBy,
            Descending = descending
        };

        return Ok(await _societies.ListAsync(query, cancellationToken));
    }

    /// <summary>Fetches a single society.</summary>
    /// <param name="id">Society identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="200">The society was found.</response>
    /// <response code="404">No society carries that identifier.</response>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(SocietyView), StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType(typeof(IntakeProblemDetails), StatusCodes.Status404NotFound, ProblemJson)]
    public async Task<ActionResult<SocietyView>> Get(Guid id, CancellationToken cancellationToken)
    {
        var society = await _societies.GetAsync(id, cancellationToken);

        if (society is null)
        {
            return NotFoundProblem(id);
        }

        return Ok(society);
    }

    /// <summary>Registers a new supplying society.</summary>
    /// <remarks>
    /// Restricted to MCC Managers and System Administrators. That restriction is declared in
    /// <see cref="IntakePolicies.ManageSocieties"/> but is not enforced until authentication
    /// lands with SCRUM-34.
    /// </remarks>
    /// <response code="201">The society was registered.</response>
    /// <response code="400">The submitted details are incomplete or invalid.</response>
    /// <response code="409">A society already uses that code.</response>
    [HttpPost]
    [ProducesResponseType(typeof(SocietyView), StatusCodes.Status201Created, "application/json")]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest, ProblemJson)]
    [ProducesResponseType(typeof(DuplicateCodeProblemDetails), StatusCodes.Status409Conflict, ProblemJson)]
    public async Task<ActionResult<SocietyView>> Create(
        [FromBody] SaveSocietyRequest request,
        CancellationToken cancellationToken)
    {
        var command = new CreateSocietyCommand(
            request.Code,
            request.Name,
            request.CanLabelPrefix,
            request.ContactPerson,
            request.ContactNumber);

        var society = await _societies.CreateAsync(command, cancellationToken);

        return CreatedAtAction(nameof(Get), new { id = society.Id }, society);
    }

    /// <summary>Amends a society.</summary>
    /// <remarks>
    /// The code can only be moved while no consignments exist against the society, because it is
    /// baked into every reference already issued. Restricted to MCC Managers and System
    /// Administrators once SCRUM-34 lands.
    /// </remarks>
    /// <response code="200">The society was updated.</response>
    /// <response code="400">The details are invalid, or the code is frozen by existing consignments.</response>
    /// <response code="404">No society carries that identifier.</response>
    /// <response code="409">Another society already uses that code.</response>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(SocietyView), StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType(typeof(IntakeProblemDetails), StatusCodes.Status400BadRequest, ProblemJson)]
    [ProducesResponseType(typeof(IntakeProblemDetails), StatusCodes.Status404NotFound, ProblemJson)]
    [ProducesResponseType(typeof(DuplicateCodeProblemDetails), StatusCodes.Status409Conflict, ProblemJson)]
    public async Task<ActionResult<SocietyView>> Update(
        Guid id,
        [FromBody] SaveSocietyRequest request,
        CancellationToken cancellationToken)
    {
        var command = new UpdateSocietyCommand(
            request.Code,
            request.Name,
            request.CanLabelPrefix,
            request.ContactPerson,
            request.ContactNumber);

        return await RouteScoped(id, () => _societies.UpdateAsync(id, command, cancellationToken));
    }

    /// <summary>Retires a society so it can no longer be selected for new consignments.</summary>
    /// <remarks>
    /// The society is kept, not deleted: historical consignments must keep resolving to it.
    /// Restricted to MCC Managers and System Administrators once SCRUM-34 lands.
    /// </remarks>
    /// <param name="id">Society identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="200">The society was retired.</response>
    /// <response code="404">No society carries that identifier.</response>
    [HttpPost("{id:guid}/deactivate")]
    [ProducesResponseType(typeof(SocietyView), StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType(typeof(IntakeProblemDetails), StatusCodes.Status404NotFound, ProblemJson)]
    public async Task<ActionResult<SocietyView>> Deactivate(Guid id, CancellationToken cancellationToken)
    {
        return await RouteScoped(id, () => _societies.DeactivateAsync(id, cancellationToken));
    }

    /// <summary>Returns a retired society to service.</summary>
    /// <param name="id">Society identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="200">The society is active again.</response>
    /// <response code="404">No society carries that identifier.</response>
    [HttpPost("{id:guid}/reactivate")]
    [ProducesResponseType(typeof(SocietyView), StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType(typeof(IntakeProblemDetails), StatusCodes.Status404NotFound, ProblemJson)]
    public async Task<ActionResult<SocietyView>> Reactivate(Guid id, CancellationToken cancellationToken)
    {
        return await RouteScoped(id, () => _societies.ReactivateAsync(id, cancellationToken));
    }

    /// <summary>
    /// Runs an action addressed by a route id. A society missing from the route is a 404 —
    /// unlike one referenced from a request body, which the exception handler answers with 422.
    /// </summary>
    private async Task<ActionResult<SocietyView>> RouteScoped(Guid id, Func<Task<SocietyView>> action)
    {
        try
        {
            return Ok(await action());
        }
        catch (EntityNotFoundException)
        {
            return NotFoundProblem(id);
        }
    }

    private ObjectResult NotFoundProblem(Guid id) => Problem(
        statusCode: StatusCodes.Status404NotFound,
        title: "Society not found",
        detail: $"No society is registered under identifier '{id}'.");
}
