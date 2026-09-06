using System.ComponentModel.DataAnnotations;
using MccIntakeService.Api.Contracts;
using MccIntakeService.Api.Infrastructure;
using MccIntakeService.Application.Tanks;
using MccIntakeService.Domain.Common;
using MccIntakeService.Domain.Tanks;
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

/// <summary>The details a tank is added or amended with (SCRUM-52).</summary>
public sealed class SaveTankRequest
{
    /// <summary>Short tank code as painted on the plant. Set once, at creation.</summary>
    /// <example>T4</example>
    [Required(ErrorMessage = "A tank code is required.")]
    [StringLength(10, MinimumLength = 1)]
    public string Code { get; set; } = string.Empty;

    /// <example>Chilling Tank 4</example>
    [Required(ErrorMessage = "A tank name is required.")]
    [StringLength(100, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Working volume in litres.</summary>
    /// <example>5000</example>
    [Range(0.01, 1000000, ErrorMessage = "A tank's capacity must be greater than zero.")]
    public decimal CapacityLitres { get; set; }
}

/// <summary>A temperature taken against a tank (SCRUM-52).</summary>
public sealed class LogTemperatureRequest
{
    /// <example>3.8</example>
    [Required(ErrorMessage = "A reading is required.")]
    [Range(-5, 40, ErrorMessage = "A tank reading must be between -5 and 40 °C.")]
    public decimal? Celsius { get; set; }
}

/// <summary>
/// The centre's chilling tanks and their manifests (SCRUM-52), so a tank's contents can be traced
/// back to the societies that supplied it.
/// </summary>
/// <remarks>
/// A tank is never deleted. It is named on every pour and dispatch note it has carried, so
/// removing the row would leave those records pointing at nothing; taking one out of service is
/// what retiring a tank means.
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
                User.OfficerIdentity(),
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

    /// <summary>Adds a tank to the centre.</summary>
    /// <response code="201">The tank was added.</response>
    /// <response code="400">The code, name or capacity is missing or out of range.</response>
    /// <response code="409">A tank already carries that code.</response>
    [HttpPost]
    [Authorize(Policy = IntakePolicies.ManageTanks)]
    [ProducesResponseType(typeof(TankView), StatusCodes.Status201Created, "application/json")]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest, ProblemJson)]
    [ProducesResponseType(typeof(IntakeProblemDetails), StatusCodes.Status409Conflict, ProblemJson)]
    public async Task<ActionResult<TankView>> Create(
        [FromBody] SaveTankRequest request,
        CancellationToken cancellationToken)
    {
        var tank = await _tanks.CreateAsync(
            new SaveTankCommand(request.Code, request.Name, request.CapacityLitres),
            cancellationToken);

        return CreatedAtAction(nameof(Manifest), new { code = tank.Code }, tank);
    }

    /// <summary>Renames a tank and restates its working volume.</summary>
    /// <remarks>
    /// The code is not amendable: it is painted on the plant and named on every pour and dispatch
    /// note the tank has carried.
    /// </remarks>
    /// <response code="200">The tank was amended.</response>
    /// <response code="400">The name or capacity is missing or out of range.</response>
    /// <response code="404">No such tank.</response>
    [HttpPut("{code}")]
    [Authorize(Policy = IntakePolicies.ManageTanks)]
    [ProducesResponseType(typeof(TankView), StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest, ProblemJson)]
    [ProducesResponseType(typeof(IntakeProblemDetails), StatusCodes.Status404NotFound, ProblemJson)]
    public async Task<ActionResult<TankView>> Update(
        string code,
        [FromBody] SaveTankRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _tanks.UpdateAsync(
                code,
                new SaveTankCommand(code, request.Name, request.CapacityLitres),
                cancellationToken));
        }
        catch (EntityNotFoundException)
        {
            return NotFoundProblem(code);
        }
    }

    /// <summary>Takes a tank out of service.</summary>
    /// <remarks>A tank still holding milk cannot be taken out: dispatch the load first.</remarks>
    /// <response code="200">The tank is out of service.</response>
    /// <response code="400">The tank still holds milk.</response>
    /// <response code="404">No such tank.</response>
    [HttpPost("{code}/deactivate")]
    [Authorize(Policy = IntakePolicies.ManageTanks)]
    [ProducesResponseType(typeof(TankView), StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType(typeof(IntakeProblemDetails), StatusCodes.Status400BadRequest, ProblemJson)]
    [ProducesResponseType(typeof(IntakeProblemDetails), StatusCodes.Status404NotFound, ProblemJson)]
    public Task<ActionResult<TankView>> Deactivate(string code, CancellationToken cancellationToken) =>
        ChangeStatus(code, TankStatus.UnderMaintenance, cancellationToken);

    /// <summary>Puts a tank back into service.</summary>
    /// <response code="200">The tank is in service.</response>
    /// <response code="404">No such tank.</response>
    [HttpPost("{code}/reactivate")]
    [Authorize(Policy = IntakePolicies.ManageTanks)]
    [ProducesResponseType(typeof(TankView), StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType(typeof(IntakeProblemDetails), StatusCodes.Status404NotFound, ProblemJson)]
    public Task<ActionResult<TankView>> Reactivate(string code, CancellationToken cancellationToken) =>
        ChangeStatus(code, TankStatus.Active, cancellationToken);

    /// <summary>Records a temperature against a tank.</summary>
    /// <remarks>
    /// Chilled milk is held at a temperature, and the reading is evidence the cold chain held. A
    /// reading is never amended once taken; a correction is another reading.
    /// </remarks>
    /// <response code="201">The reading was recorded.</response>
    /// <response code="400">The reading is missing or outside the range an instrument reports.</response>
    /// <response code="404">No such tank.</response>
    [HttpPost("{code}/temperatures")]
    [ProducesResponseType(typeof(TankTemperatureView), StatusCodes.Status201Created, "application/json")]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest, ProblemJson)]
    [ProducesResponseType(typeof(IntakeProblemDetails), StatusCodes.Status404NotFound, ProblemJson)]
    public async Task<ActionResult<TankTemperatureView>> LogTemperature(
        string code,
        [FromBody] LogTemperatureRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var reading = await _tanks.RecordTemperatureAsync(
                code,
                request.Celsius!.Value,
                User.OfficerIdentity(),
                cancellationToken);

            return CreatedAtAction(nameof(Temperatures), new { code }, reading);
        }
        catch (EntityNotFoundException)
        {
            return NotFoundProblem(code);
        }
    }

    /// <summary>The readings taken against a tank, newest first.</summary>
    /// <param name="code">Tank code.</param>
    /// <param name="limit">How many readings to return, capped at 200.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="200">The readings.</response>
    /// <response code="404">No such tank.</response>
    [HttpGet("{code}/temperatures")]
    [ProducesResponseType(typeof(IReadOnlyList<TankTemperatureView>), StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType(typeof(IntakeProblemDetails), StatusCodes.Status404NotFound, ProblemJson)]
    public async Task<ActionResult<IReadOnlyList<TankTemperatureView>>> Temperatures(
        string code,
        CancellationToken cancellationToken,
        [FromQuery] int limit = 20)
    {
        var readings = await _tanks.TemperaturesAsync(code, limit, cancellationToken);

        return readings is null ? NotFoundProblem(code) : Ok(readings);
    }

    private async Task<ActionResult<TankView>> ChangeStatus(
        string code,
        TankStatus status,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _tanks.ChangeStatusAsync(code, status, cancellationToken));
        }
        catch (EntityNotFoundException)
        {
            return NotFoundProblem(code);
        }
    }

    private ObjectResult NotFoundProblem(string code) => this.IntakeProblem(
        StatusCodes.Status404NotFound,
        "entity_not_found",
        "Tank not found",
        $"No chilling tank is registered under code '{code}'.");
}
