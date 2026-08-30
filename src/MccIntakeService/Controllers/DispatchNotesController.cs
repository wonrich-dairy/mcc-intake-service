using System.ComponentModel.DataAnnotations;
using MccIntakeService.Api.Contracts;
using MccIntakeService.Api.Infrastructure;
using MccIntakeService.Application.Dispatch;
using MccIntakeService.Domain.Common;
using MccIntakeService.Domain.Dispatch;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wonrich.Auth.Tokens;
using Wonrich.QualityPanel;

namespace MccIntakeService.Controllers;

/// <summary>One tank the bowser drew from.</summary>
public sealed class DispatchDrawRequest
{
    /// <summary>Tank code, e.g. <c>T1</c>.</summary>
    /// <example>T1</example>
    [Required(ErrorMessage = "A tank code is required.")]
    [StringLength(10, MinimumLength = 1)]
    public string TankCode { get; set; } = string.Empty;

    /// <summary>Litres drawn from that tank.</summary>
    /// <example>1200.00</example>
    [Range(0.01, 100000, ErrorMessage = "The quantity drawn must be greater than zero.")]
    public decimal QuantityLitres { get; set; }
}

/// <summary>The dispatch note a manager submits when a bowser is loaded (SCRUM-8).</summary>
public sealed class RecordDispatchNoteRequest
{
    /// <example>WP-CAB-1234</example>
    [Required(ErrorMessage = "A bowser registration is required.")]
    [StringLength(DispatchNote.MaxBowserRegistrationLength, MinimumLength = 1)]
    public string BowserRegistration { get; set; } = string.Empty;

    /// <example>Ranjith Fernando</example>
    [Required(ErrorMessage = "A driver name is required.")]
    [StringLength(DispatchNote.MaxDriverNameLength, MinimumLength = 1)]
    public string DriverName { get; set; } = string.Empty;

    /// <summary>Wall-clock dispatch time. Omit and the server captures the current time.</summary>
    /// <example>2026-08-23T14:30:00</example>
    public DateTime? DispatchedAtLocal { get; set; }

    /// <summary>The tanks drawn from. At least one is required.</summary>
    [Required(ErrorMessage = "At least one source tank is required.")]
    [MinLength(1, ErrorMessage = "At least one source tank is required.")]
    public List<DispatchDrawRequest> Draws { get; set; } = [];

    // AC5 requires the panel to be recorded, so every reading is required rather than defaulted.
    // Bound as a non-nullable decimal, an omitted field would bind to 0, sit inside its own range
    // and read back as a measurement nobody took — and 0 is a plausible-looking figure on three
    // of these five. Nullable plus [Required] is what makes the omission a 400.

    /// <example>4.0</example>
    [Required(ErrorMessage = "The fat reading is required.")]
    [Range(0, 15, ErrorMessage = "Fat must be between 0 and 15 percent.")]
    public decimal? FatPercent { get; set; }

    /// <example>8.6</example>
    [Required(ErrorMessage = "The SNF reading is required.")]
    [Range(0, 15, ErrorMessage = "SNF must be between 0 and 15.")]
    public decimal? Snf { get; set; }

    /// <example>Blue</example>
    [Required(ErrorMessage = "The KQ colour is required.")]
    [EnumDataType(typeof(KqColour), ErrorMessage = "That is not a shade on the KQ card.")]
    public KqColour? KqColour { get; set; }

    /// <example>Stable</example>
    [Required(ErrorMessage = "The alcohol stability grade is required.")]
    [EnumDataType(typeof(StabilityGrade), ErrorMessage = "That is not a grade on the stability scale.")]
    public StabilityGrade? StabilityGrade { get; set; }

    /// <example>4.5</example>
    [Required(ErrorMessage = "The dispatch temperature is required.")]
    [Range(0, 50, ErrorMessage = "The temperature must be between 0 and 50 °C.")]
    public decimal? TemperatureCelsius { get; set; }

    /// <example>Loaded from T1 and T2 after the morning collection.</example>
    [StringLength(DispatchNote.MaxRemarksLength)]
    public string? Remarks { get; set; }

    /// <summary>Maps the request onto the application command.</summary>
    public RecordDispatchCommand ToCommand(string? dispatchedBy) => new(
        BowserRegistration,
        DriverName,
        Draws.Select(draw => new DispatchDrawCommand(draw.TankCode, draw.QuantityLitres)).ToList(),
        FatPercent!.Value,
        Snf!.Value,
        KqColour!.Value,
        StabilityGrade!.Value,
        TemperatureCelsius!.Value,
        Remarks,
        DispatchedAtLocal,
        dispatchedBy);
}

/// <summary>
/// Bowser dispatch notes (SCRUM-8): which tanks a bowser was loaded from, how much came from
/// each, and the panel taken at loading.
/// </summary>
/// <remarks>
/// A note is written once and read thereafter — it is the handover the factory works from.
/// Submitting one closes the fill of every tank it empties: no further pours join a load that has
/// already left, and the tank goes on to hold the next one.
/// </remarks>
[ApiController]
[Route("api/dispatch-notes")]
[Authorize(Policy = IntakePolicies.RecordDispatchNotes)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
public class DispatchNotesController : ControllerBase
{
    private const string ProblemJson = "application/problem+json";

    private readonly IDispatchService _dispatch;

    public DispatchNotesController(IDispatchService dispatch)
    {
        _dispatch = dispatch;
    }

    /// <summary>Lists dispatch notes, optionally for one dispatch date.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="date">Restrict to a single dispatch date.</param>
    /// <response code="200">The matching notes, newest first.</response>
    /// <response code="400"><c>date</c> was supplied but is not a date.</response>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<DispatchNoteView>), StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest, ProblemJson)]
    public async Task<ActionResult<IReadOnlyList<DispatchNoteView>>> List(
        CancellationToken cancellationToken,
        [FromQuery] DateOnly? date = null) =>
        Ok(await _dispatch.ListAsync(date, cancellationToken));

    /// <summary>Fetches one note, resolved to the consignments that contributed to it.</summary>
    /// <response code="200">The dispatch note.</response>
    /// <response code="404">No note carries that reference.</response>
    [HttpGet("{reference}")]
    [ProducesResponseType(typeof(DispatchNoteView), StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType(typeof(IntakeProblemDetails), StatusCodes.Status404NotFound, ProblemJson)]
    public async Task<ActionResult<DispatchNoteView>> Get(string reference, CancellationToken cancellationToken)
    {
        var note = await _dispatch.GetAsync(reference, cancellationToken);

        if (note is null)
        {
            return this.IntakeProblem(
                StatusCodes.Status404NotFound,
                "entity_not_found",
                "Dispatch note not found",
                $"No dispatch note carries the reference '{reference}'.");
        }

        return Ok(note);
    }

    /// <summary>Records a dispatch note, closing the fill of every tank it empties.</summary>
    /// <remarks>
    /// The total is calculated from the per-tank quantities, and a tank cannot give up more than
    /// its current fill holds. The note is read-only once submitted.
    /// </remarks>
    /// <response code="201">The note was recorded and the fills it emptied closed.</response>
    /// <response code="400">A quantity exceeds what the tank holds, or the dispatch time is out of range.</response>
    /// <response code="404">One of the tank codes is not a known tank.</response>
    [HttpPost]
    [ProducesResponseType(typeof(DispatchNoteView), StatusCodes.Status201Created, "application/json")]
    [ProducesResponseType(typeof(IntakeProblemDetails), StatusCodes.Status400BadRequest, ProblemJson)]
    [ProducesResponseType(typeof(IntakeProblemDetails), StatusCodes.Status404NotFound, ProblemJson)]
    public async Task<ActionResult<DispatchNoteView>> Record(
        [FromBody] RecordDispatchNoteRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var note = await _dispatch.RecordAsync(
                request.ToCommand(User.UserId() ?? User.UserName()),
                cancellationToken);

            return CreatedAtAction(nameof(Get), new { reference = note.Reference }, note);
        }
        catch (EntityNotFoundException exception)
        {
            // Addressed by the request rather than the route, but a tank code the caller chose is
            // still a 404 here, as it is on the tank routes.
            return this.IntakeProblem(
                StatusCodes.Status404NotFound,
                exception.Code,
                "Tank not found",
                exception.Message);
        }
    }
}
