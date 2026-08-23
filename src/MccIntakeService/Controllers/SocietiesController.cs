using MccIntakeService.Application.Societies;
using Microsoft.AspNetCore.Mvc;

namespace MccIntakeService.Controllers;

/// <summary>
/// Registered supplying societies. Read-only for now: the intake officer picks a society from
/// this list rather than typing one. Society management is delivered by SCRUM-51.
/// </summary>
[ApiController]
[Route("api/societies")]
[Produces("application/json")]
public class SocietiesController : ControllerBase
{
    private readonly ISocietyService _societies;

    public SocietiesController(ISocietyService societies)
    {
        _societies = societies;
    }

    /// <summary>Lists societies available for selection at intake.</summary>
    /// <param name="includeInactive">Include societies that no longer supply milk.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="200">The registered societies.</response>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<SocietyView>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<SocietyView>>> List(
        CancellationToken cancellationToken,
        [FromQuery] bool includeInactive = false)
    {
        return Ok(await _societies.ListAsync(!includeInactive, cancellationToken));
    }

    /// <summary>Fetches a single society.</summary>
    /// <param name="id">Society identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="200">The society was found.</response>
    /// <response code="404">No society carries that identifier.</response>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(SocietyView), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SocietyView>> Get(Guid id, CancellationToken cancellationToken)
    {
        var society = await _societies.GetAsync(id, cancellationToken);

        if (society is null)
        {
            return Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Society not found",
                detail: $"No society is registered under identifier '{id}'.");
        }

        return Ok(society);
    }
}
