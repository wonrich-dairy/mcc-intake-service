using System.ComponentModel.DataAnnotations;
using MccIntakeService.Api.Contracts;
using MccIntakeService.Api.Infrastructure;
using MccIntakeService.Application.Factory;
using MccIntakeService.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wonrich.Auth.Tokens;

namespace MccIntakeService.Controllers;

/// <summary>The screening an officer records for an arriving bowser (SCRUM-9).</summary>
public sealed class ScreenArrivalRequest
{
    /// <summary>Reference of the dispatch note the bowser arrived on, entered or scanned.</summary>
    /// <example>DN-20260823-01</example>
    [Required(ErrorMessage = "A dispatch note reference is required.")]
    [StringLength(30, MinimumLength = 1)]
    public string DispatchNoteReference { get; set; } = string.Empty;

    /// <summary>Wall-clock arrival time. Omit and the server captures the current time.</summary>
    /// <example>2026-08-23T16:10:00</example>
    public DateTime? ArrivedAtLocal { get; set; }

    /// <summary>Whether the milk smelled sound.</summary>
    /// <example>true</example>
    public bool SmellPassed { get; set; }

    /// <summary>Whether the colour was acceptable.</summary>
    /// <example>true</example>
    public bool ColourPassed { get; set; }

    /// <summary>Whether the arrival temperature was within limits.</summary>
    /// <example>true</example>
    public bool TemperaturePassed { get; set; }

    /// <summary>Temperature measured on arrival, in °C.</summary>
    /// <example>4.8</example>
    [Range(0, 50, ErrorMessage = "The arrival temperature must be between 0 and 50 °C.")]
    public decimal TemperatureCelsius { get; set; }

    /// <summary>Maps the request onto the application command.</summary>
    public ScreenArrivalCommand ToCommand(string? screenedBy) => new(
        DispatchNoteReference,
        SmellPassed,
        ColourPassed,
        TemperaturePassed,
        TemperatureCelsius,
        ArrivedAtLocal,
        screenedBy);
}

/// <summary>
/// Factory intake (SCRUM-9): screening an arriving bowser on smell, colour and temperature, and
/// creating the production batch when it passes.
/// </summary>
/// <remarks>
/// A screening is recorded either way. A failure on any parameter blocks the batch and leaves the
/// rejection on record, so spoiled milk never enters the system as something production can draw
/// on, and the turn-away is still traceable.
/// </remarks>
[ApiController]
[Route("api/factory")]
[Authorize(Policy = IntakePolicies.ScreenFactoryArrivals)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
public class FactoryIntakeController : ControllerBase
{
    private const string ProblemJson = "application/problem+json";

    private readonly IFactoryIntakeService _factory;

    public FactoryIntakeController(IFactoryIntakeService factory)
    {
        _factory = factory;
    }

    /// <summary>Screens an arriving bowser, creating a batch only when every check passes.</summary>
    /// <remarks>
    /// The response carries the outcome either way: on a pass it includes the new batch, and on a
    /// rejection it names the parameters that failed.
    /// </remarks>
    /// <response code="201">The screening was recorded. A batch is present when it passed.</response>
    /// <response code="400">The request is invalid.</response>
    /// <response code="404">No dispatch note carries that reference.</response>
    /// <response code="409">That dispatch note has already been screened.</response>
    [HttpPost("arrivals")]
    [ProducesResponseType(typeof(ArrivalScreeningView), StatusCodes.Status201Created, "application/json")]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest, ProblemJson)]
    [ProducesResponseType(typeof(IntakeProblemDetails), StatusCodes.Status404NotFound, ProblemJson)]
    [ProducesResponseType(typeof(IntakeProblemDetails), StatusCodes.Status409Conflict, ProblemJson)]
    public async Task<ActionResult<ArrivalScreeningView>> Screen(
        [FromBody] ScreenArrivalRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var screening = await _factory.ScreenAsync(
                request.ToCommand(User.OfficerIdentity()),
                cancellationToken);

            return screening.Batch is null
                ? Created(string.Empty, screening)
                : CreatedAtAction(nameof(GetBatch), new { reference = screening.Batch.Reference }, screening);
        }
        catch (EntityNotFoundException)
        {
            return Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Dispatch note not found",
                detail: $"No dispatch note carries the reference '{request.DispatchNoteReference}'.");
        }
        catch (DomainValidationException exception) when (exception.Message.Contains(
            "already been screened", StringComparison.OrdinalIgnoreCase))
        {
            return Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Arrival already screened",
                detail: exception.Message);
        }
    }

    /// <summary>Lists batches, optionally by date or by originating dispatch note.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="date">Restrict to a single batch date.</param>
    /// <param name="dispatchNote">Restrict to the batch from one dispatch note.</param>
    /// <response code="200">The matching batches, newest first.</response>
    [HttpGet("batches")]
    [ProducesResponseType(typeof(IReadOnlyList<BatchView>), StatusCodes.Status200OK, "application/json")]
    public async Task<ActionResult<IReadOnlyList<BatchView>>> ListBatches(
        CancellationToken cancellationToken,
        [FromQuery] DateOnly? date = null,
        [FromQuery] string? dispatchNote = null) =>
        Ok(await _factory.ListBatchesAsync(date, dispatchNote, cancellationToken));

    /// <summary>Fetches one batch by its reference.</summary>
    /// <response code="200">The batch.</response>
    /// <response code="404">No batch carries that reference.</response>
    [HttpGet("batches/{reference}")]
    [ProducesResponseType(typeof(BatchView), StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType(typeof(IntakeProblemDetails), StatusCodes.Status404NotFound, ProblemJson)]
    public async Task<ActionResult<BatchView>> GetBatch(string reference, CancellationToken cancellationToken)
    {
        var batch = await _factory.GetBatchAsync(reference, cancellationToken);

        if (batch is null)
        {
            return Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Batch not found",
                detail: $"No batch carries the reference '{reference}'.");
        }

        return Ok(batch);
    }
}
