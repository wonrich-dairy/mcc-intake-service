using System.ComponentModel.DataAnnotations;
using MccIntakeService.Api.Contracts;
using MccIntakeService.Api.Infrastructure;
using MccIntakeService.Application.Sync;
using MccIntakeService.Domain.QualityTests;
using MccIntakeService.Domain.Sync;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wonrich.Auth.Tokens;
using Wonrich.QualityPanel;

namespace MccIntakeService.Controllers;

/// <summary>One can on an offline consignment sheet.</summary>
public sealed class SyncCanRequest
{
    /// <example>1</example>
    [Range(1, 999, ErrorMessage = "Can number must be between 1 and 999.")]
    public int CanNumber { get; set; }

    /// <example>41.2</example>
    [Range(0.01, 1000, ErrorMessage = "Quantity must be greater than zero and no more than 1000 kilograms.")]
    public decimal QuantityKg { get; set; }
}

/// <summary>A consignment captured offline.</summary>
public sealed class SyncConsignmentRequest
{
    [Required]
    public Guid SocietyId { get; set; }

    [Required(ErrorMessage = "At least one can must be recorded.")]
    [MinLength(1, ErrorMessage = "At least one can must be recorded.")]
    public List<SyncCanRequest> Cans { get; set; } = [];

    /// <summary>Arrival time captured on the device while offline.</summary>
    public DateTime? ArrivalAtLocal { get; set; }
}

/// <summary>A gate panel captured offline.</summary>
public sealed class SyncQualityTestRequest
{
    [Required]
    [StringLength(40, MinimumLength = 1)]
    public string ConsignmentReference { get; set; } = string.Empty;

    [Range(0, 15)]
    public decimal FatPercent { get; set; }

    [Range(0, 40)]
    public decimal RawLactometerReading { get; set; }

    [Range(0, 50)]
    public decimal TemperatureCelsius { get; set; }

    [Range(0, 100)]
    public decimal WaterPercent { get; set; }

    public KqColour KqColour { get; set; }

    /// <summary>The officer's own senses, defaulting to a sound sample.</summary>
    public bool SmellOk { get; set; } = true;

    public bool ColourOk { get; set; } = true;

    public bool TasteOk { get; set; } = true;

    [Required]
    [MinLength(1, ErrorMessage = "At least the 80% alcohol result is required.")]
    public Dictionary<AlcoholStage, StageOutcome> AlcoholOutcomes { get; set; } = [];

    public TestVerdict Verdict { get; set; }

    [StringLength(50)]
    public string? FailedParameter { get; set; }

    [StringLength(50)]
    public string? FailedValue { get; set; }
}

/// <summary>A pour captured offline.</summary>
public sealed class SyncPourRequest
{
    [Required]
    [StringLength(10, MinimumLength = 1)]
    public string TankCode { get; set; } = string.Empty;

    [Required]
    [StringLength(40, MinimumLength = 1)]
    public string ConsignmentReference { get; set; } = string.Empty;
}

/// <summary>One record from the officer's offline queue.</summary>
public sealed class SyncOperationRequest
{
    /// <summary>
    /// The client's own identifier for the record, stable across retries. This is what makes the
    /// upload idempotent, so it must not change if the queue is sent again.
    /// </summary>
    /// <example>3f1b6a2c-8d4e-4a91-9f22-7c5e1a0b4d8e</example>
    [Required(ErrorMessage = "A client record identifier is required.")]
    [StringLength(100, MinimumLength = 1)]
    public string ClientRecordId { get; set; } = string.Empty;

    /// <summary>Position in the queue. Records are applied in ascending order.</summary>
    /// <example>1</example>
    public int Sequence { get; set; }

    /// <example>RegisterConsignment</example>
    public SyncOperationKind Kind { get; set; }

    public SyncConsignmentRequest? Consignment { get; set; }

    public SyncQualityTestRequest? QualityTest { get; set; }

    public SyncPourRequest? Pour { get; set; }
}

/// <summary>An officer's offline queue, uploaded on reconnection.</summary>
public sealed class SyncUploadRequest
{
    [Required(ErrorMessage = "At least one record is required.")]
    [MinLength(1, ErrorMessage = "At least one record is required.")]
    public List<SyncOperationRequest> Operations { get; set; } = [];

    /// <summary>Maps the request onto the application operations.</summary>
    public IReadOnlyCollection<SyncOperation> ToOperations() =>
        Operations.Select(operation => new SyncOperation(
            operation.ClientRecordId,
            operation.Sequence,
            operation.Kind,
            operation.Consignment is null
                ? null
                : new SyncConsignmentPayload(
                    operation.Consignment.SocietyId,
                    operation.Consignment.Cans
                        .Select(can => new CanEntryPayload(can.CanNumber, can.QuantityKg))
                        .ToList(),
                    operation.Consignment.ArrivalAtLocal),
            operation.QualityTest is null
                ? null
                : new SyncQualityTestPayload(
                    operation.QualityTest.ConsignmentReference,
                    operation.QualityTest.FatPercent,
                    operation.QualityTest.RawLactometerReading,
                    operation.QualityTest.TemperatureCelsius,
                    operation.QualityTest.WaterPercent,
                    operation.QualityTest.KqColour,
                    operation.QualityTest.AlcoholOutcomes,
                    operation.QualityTest.Verdict,
                    operation.QualityTest.FailedParameter,
                    operation.QualityTest.FailedValue,
                    operation.QualityTest.SmellOk,
                    operation.QualityTest.ColourOk,
                    operation.QualityTest.TasteOk),
            operation.Pour is null
                ? null
                : new SyncPourPayload(operation.Pour.TankCode, operation.Pour.ConsignmentReference)))
            .ToList();
}

/// <summary>
/// Uploads records an officer captured while offline (SCRUM-10), so intake is never blocked by
/// network conditions at the centre.
/// </summary>
/// <remarks>
/// <para>
/// The upload is idempotent on the client's own record identifier. A handheld that drops
/// connectivity mid-upload cannot know whether the server took a record, so its only safe move is
/// to send the queue again — the server is the side that has to remember.
/// </para>
/// <para>
/// Records apply in the order the client created them, and each is reported separately: one bad
/// record never sinks the queue behind it, and the client keeps it for manual review rather than
/// discarding it.
/// </para>
/// <para>
/// References are always issued by the server, exactly as they are online, so a record captured
/// offline cannot collide with one the server has already handed out.
/// </para>
/// </remarks>
[ApiController]
[Route("api/sync")]
[Authorize(Policy = IntakePolicies.RegisterConsignments)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
public class SyncController : ControllerBase
{
    private const string ProblemJson = "application/problem+json";

    private readonly ISyncService _sync;

    public SyncController(ISyncService sync)
    {
        _sync = sync;
    }

    /// <summary>Uploads a queue of offline records.</summary>
    /// <remarks>
    /// Always answers <c>200</c> when the request itself is well formed: the per-record statuses
    /// carry the outcome. A record already uploaded comes back as <c>Duplicate</c> with the
    /// reference the first attempt produced.
    /// </remarks>
    /// <response code="200">Every record's outcome, in the order submitted.</response>
    /// <response code="400">The upload itself is malformed.</response>
    [HttpPost]
    [ProducesResponseType(typeof(SyncBatchResult), StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest, ProblemJson)]
    public async Task<ActionResult<SyncBatchResult>> Upload(
        [FromBody] SyncUploadRequest request,
        CancellationToken cancellationToken) =>
        Ok(await _sync.UploadAsync(
            request.ToOperations(),
            User.UserId() ?? User.UserName(),
            cancellationToken));
}
