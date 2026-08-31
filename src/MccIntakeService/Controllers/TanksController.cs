using System.ComponentModel.DataAnnotations;
using MccIntakeService.Api.Contracts;
using MccIntakeService.Api.Infrastructure;
using MccIntakeService.Application.Tanks;
using MccIntakeService.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wonrich.Auth.Tokens;

namespace MccIntakeService.Controllers;

/// <summary>The consignment being poured into a tank.</summary>
public sealed class PourRequest
{
    /// <summary>
    /// Gate reference of an accepted consignment, chosen from <c>GET /api/tanks/pourable</c>.
    /// </summary>
    /// <example>MCC-20260823-KC-01</example>
    [Required(ErrorMessage = "A consignment reference is required.")]
    [StringLength(40, MinimumLength = 1)]
    public string ConsignmentReference { get; set; } = string.Empty;
}

/// <summary>
/// The centre's chilling tanks and their manifests (SCRUM-52), so a tank's contents can be traced
/// back to the societies that supplied it.
/// </summary>
/// <remarks>
/// The three tanks are plant rather than reference data, so they ship with the schema and there
/// is no endpoint to add or remove one.
/// </remarks>
[ApiController]
[Route("api/tanks")]
[Authorize(Policy = IntakePolicies.PourToTanks)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
public class TanksController : ControllerBase
{
    private const string ProblemJson = "application/problem+json";

    private readonly ITankService _tanks;

    public TanksController(ITankService tanks)
    {
        _tanks = tanks;
    }

    /// <summary>Lists the tanks with their running totals.</summary>
    /// <response code="200">The tanks and what each currently holds.</response>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<TankView>), StatusCodes.Status200OK, "application/json")]
    public async Task<ActionResult<IReadOnlyList<TankView>>> List(CancellationToken cancellationToken) =>
        Ok(await _tanks.ListAsync(cancellationToken));

    /// <summary>
    /// Lists the consignments that may be poured: accepted at the gate and not already in a tank.
    /// </summary>
    /// <remarks>
    /// Rejected and untested consignments never appear here, so they cannot be selected.
    /// </remarks>
    /// <response code="200">The consignments available to pour.</response>
    [HttpGet("pourable")]
    [ProducesResponseType(typeof(IReadOnlyList<PourableConsignmentView>), StatusCodes.Status200OK, "application/json")]
    public async Task<ActionResult<IReadOnlyList<PourableConsignmentView>>> Pourable(
        CancellationToken cancellationToken) =>
        Ok(await _tanks.PourableAsync(cancellationToken));

    /// <summary>Reads a tank's manifest, optionally for a single pour date.</summary>
    /// <param name="code">Tank code, e.g. <c>T1</c>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="date">Restrict the entries to one pour date; totals always cover the whole tank.</param>
    /// <response code="200">The manifest and the tank's running totals.</response>
    /// <response code="400"><c>date</c> was supplied but is not a date.</response>
    /// <response code="404">No tank carries that code.</response>
    [HttpGet("{code}/manifest")]
    [ProducesResponseType(typeof(TankManifestView), StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest, ProblemJson)]
    [ProducesResponseType(typeof(IntakeProblemDetails), StatusCodes.Status404NotFound, ProblemJson)]
    public async Task<ActionResult<TankManifestView>> Manifest(
        string code,
        CancellationToken cancellationToken,
        [FromQuery] DateOnly? date = null)
    {
        var manifest = await _tanks.ManifestAsync(code, date, cancellationToken);

        return manifest is null ? NotFoundProblem(code) : Ok(manifest);
    }

    /// <summary>Pours an accepted consignment into the tank and returns the updated manifest.</summary>
    /// <remarks>
    /// Only a consignment accepted at the gate can be poured, and it goes into exactly one tank.
    /// The pour time and the officer's identity are recorded with the entry.
    /// </remarks>
    /// <response code="201">The pour was recorded.</response>
    /// <response code="400">The consignment was rejected or has not been tested.</response>
    /// <response code="404">No such tank, or no such consignment.</response>
    /// <response code="409">That consignment has already been poured.</response>
    [HttpPost("{code}/pours")]
    [ProducesResponseType(typeof(TankManifestView), StatusCodes.Status201Created, "application/json")]
    [ProducesResponseType(typeof(IntakeProblemDetails), StatusCodes.Status400BadRequest, ProblemJson)]
    [ProducesResponseType(typeof(IntakeProblemDetails), StatusCodes.Status404NotFound, ProblemJson)]
    [ProducesResponseType(typeof(IntakeProblemDetails), StatusCodes.Status409Conflict, ProblemJson)]
    public async Task<ActionResult<TankManifestView>> Pour(
        string code,
        [FromBody] PourRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var manifest = await _tanks.PourAsync(
                code,
                request.ConsignmentReference,
                User.UserId() ?? User.UserName(),
                cancellationToken);

            return CreatedAtAction(nameof(Manifest), new { code }, manifest);
        }
        catch (EntityNotFoundException exception)
        {
            // Addressed by the route, so 404 rather than the 422 the handler gives a body
            // reference. The conflict and the rejected-consignment 400 both reach the handler,
            // which writes them from their own codes.
            return this.IntakeProblem(
                StatusCodes.Status404NotFound,
                exception.Code,
                "Not found",
                exception.Message);
        }
    }

    private ObjectResult NotFoundProblem(string code) => this.IntakeProblem(
        StatusCodes.Status404NotFound,
        "entity_not_found",
        "Tank not found",
        $"No chilling tank is registered under code '{code}'.");
}
