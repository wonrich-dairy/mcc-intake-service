using MccIntakeService.Domain.Common;
using MccIntakeService.Domain.Dispatch;

namespace MccIntakeService.Domain.Factory;

/// <summary>How a screened bowser was dealt with at the factory gate.</summary>
public enum ScreeningOutcome
{
    /// <summary>Every parameter passed; the milk was unloaded as a batch.</summary>
    Accepted = 0,

    /// <summary>At least one parameter failed; the bowser was turned away.</summary>
    Rejected = 1
}

/// <summary>The three checks a bowser is screened on before unloading (SCRUM-9).</summary>
/// <param name="SmellPassed">Whether the milk smelled sound.</param>
/// <param name="ColourPassed">Whether the colour was acceptable.</param>
/// <param name="TemperaturePassed">Whether the arrival temperature was within limits.</param>
/// <param name="TemperatureCelsius">Temperature actually measured on arrival.</param>
public sealed record ScreeningChecks(
    bool SmellPassed,
    bool ColourPassed,
    bool TemperaturePassed,
    decimal TemperatureCelsius)
{
    /// <summary>The parameters that failed, in the order the officer works through them.</summary>
    public IReadOnlyList<string> FailedParameters
    {
        get
        {
            var failed = new List<string>();

            if (!SmellPassed)
            {
                failed.Add("Smell");
            }

            if (!ColourPassed)
            {
                failed.Add("Colour");
            }

            if (!TemperaturePassed)
            {
                failed.Add("Temperature");
            }

            return failed;
        }
    }

    /// <summary>Whether every check passed.</summary>
    public bool AllPassed => FailedParameters.Count == 0;
}

/// <summary>
/// The screening of an arriving bowser at factory intake (SCRUM-9). Recorded whether the milk is
/// taken or turned away, so a rejection leaves a trail rather than nothing at all.
/// </summary>
/// <remarks>
/// A dispatch note is screened once. Re-screening a note that was already dealt with would leave
/// two answers about the same bowser, and the batch it may have produced would no longer resolve
/// to a single arrival.
/// </remarks>
public class ArrivalScreening
{
    /// <summary>EF Core materialisation constructor.</summary>
    private ArrivalScreening()
    {
    }

    private ArrivalScreening(
        Guid id,
        Guid dispatchNoteId,
        DateTime arrivedAtLocal,
        ScreeningChecks checks,
        string? failedParameters,
        string? screenedBy,
        DateTimeOffset screenedAtUtc)
    {
        Id = id;
        DispatchNoteId = dispatchNoteId;
        ArrivedAtLocal = arrivedAtLocal;
        ArrivalDate = DateOnly.FromDateTime(arrivedAtLocal);

        SmellPassed = checks.SmellPassed;
        ColourPassed = checks.ColourPassed;
        TemperaturePassed = checks.TemperaturePassed;
        TemperatureCelsius = checks.TemperatureCelsius;

        Outcome = checks.AllPassed ? ScreeningOutcome.Accepted : ScreeningOutcome.Rejected;
        FailedParameters = failedParameters;
        ScreenedBy = screenedBy;
        ScreenedAtUtc = screenedAtUtc.UtcDateTime;
    }

    public Guid Id { get; private set; }

    public Guid DispatchNoteId { get; private set; }

    public DispatchNote? DispatchNote { get; private set; }

    /// <summary>Wall-clock time the bowser reached the factory.</summary>
    public DateTime ArrivedAtLocal { get; private set; }

    /// <summary>Local arrival date, so screenings can be listed by day.</summary>
    public DateOnly ArrivalDate { get; private set; }

    public bool SmellPassed { get; private set; }

    public bool ColourPassed { get; private set; }

    public bool TemperaturePassed { get; private set; }

    public decimal TemperatureCelsius { get; private set; }

    public ScreeningOutcome Outcome { get; private set; }

    /// <summary>Comma-separated parameters that failed; null when the screening passed.</summary>
    public string? FailedParameters { get; private set; }

    /// <summary>Identity of the factory intake officer.</summary>
    public string? ScreenedBy { get; private set; }

    public DateTime ScreenedAtUtc { get; private set; }

    /// <summary>The batch created when the screening passed; absent on a rejection.</summary>
    public Batch? Batch { get; private set; }

    /// <summary>
    /// Screens an arriving bowser. A failure on any parameter is recorded and blocks the batch;
    /// a clean pass creates one.
    /// </summary>
    /// <param name="id">Identity for the screening.</param>
    /// <param name="dispatchNote">The dispatch note the bowser arrived on.</param>
    /// <param name="arrivedAtLocal">Wall-clock arrival time.</param>
    /// <param name="checks">The three screening results.</param>
    /// <param name="batchReference">Reference to give the batch, used only when it passes.</param>
    /// <param name="screenedBy">Factory intake officer identifier.</param>
    /// <param name="nowLocal">Current wall-clock time at the factory.</param>
    /// <param name="screenedAtUtc">Instant the screening was submitted.</param>
    public static ArrivalScreening Screen(
        Guid id,
        DispatchNote dispatchNote,
        DateTime arrivedAtLocal,
        ScreeningChecks checks,
        string batchReference,
        string? screenedBy,
        DateTime nowLocal,
        DateTimeOffset screenedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(dispatchNote);
        ArgumentNullException.ThrowIfNull(checks);

        EnsureArrivalIsScreenable(arrivedAtLocal, dispatchNote.DispatchedAtLocal, nowLocal);

        var failed = checks.FailedParameters;

        var screening = new ArrivalScreening(
            id,
            dispatchNote.Id,
            arrivedAtLocal,
            checks,
            failed.Count == 0 ? null : string.Join(", ", failed),
            screenedBy,
            screenedAtUtc);

        if (!checks.AllPassed)
        {
            // The rejection is the record. No batch is created, so spoiled milk never enters the
            // system as something production could later draw on.
            return screening;
        }

        if (string.IsNullOrWhiteSpace(batchReference))
        {
            throw new DomainValidationException("A batch reference is required to accept an arrival.");
        }

        screening.Batch = Batch.Create(
            Guid.NewGuid(),
            batchReference,
            screening.Id,
            dispatchNote.Id,
            arrivedAtLocal,
            screenedAtUtc);

        return screening;
    }

    /// <summary>
    /// Validates the arrival time on its own, so a caller can reject one before spending a round
    /// trip issuing a batch reference. <see cref="Screen"/> re-runs this check.
    /// </summary>
    /// <param name="arrivedAtLocal">Wall-clock arrival time as captured.</param>
    /// <param name="dispatchedAtLocal">When the bowser left the chilling centre.</param>
    /// <param name="nowLocal">Current wall-clock time at the factory.</param>
    public static void EnsureArrivalIsScreenable(
        DateTime arrivedAtLocal,
        DateTime dispatchedAtLocal,
        DateTime nowLocal)
    {
        // The officer may correct the captured arrival time, but only backwards: a bowser cannot
        // arrive in the future. Left unbounded this dates the batch reference too, on a record
        // production then works from. The skew allowance matches the gate's.
        if (arrivedAtLocal > nowLocal.AddMinutes(1))
        {
            throw new DomainValidationException(
                $"Arrival time {arrivedAtLocal:yyyy-MM-dd HH:mm} is in the future and cannot be recorded.");
        }

        // And it cannot have arrived before it left. The dispatch note is the other half of the
        // same journey, so the bound comes from the record rather than from a guessed window.
        if (arrivedAtLocal < dispatchedAtLocal)
        {
            throw new DomainValidationException(
                $"Arrival time {arrivedAtLocal:yyyy-MM-dd HH:mm} is before the bowser was dispatched "
                + $"at {dispatchedAtLocal:yyyy-MM-dd HH:mm}.");
        }
    }
}

/// <summary>
/// A production batch created when an arriving bowser passes screening (SCRUM-9). It is the unit
/// production works in, and it resolves back through the dispatch note to the source tanks.
/// </summary>
public class Batch
{
    /// <summary>EF Core materialisation constructor.</summary>
    private Batch()
    {
        Reference = string.Empty;
    }

    private Batch(
        Guid id,
        string reference,
        Guid arrivalScreeningId,
        Guid dispatchNoteId,
        DateTime createdAtLocal,
        DateTimeOffset createdAtUtc)
    {
        Id = id;
        Reference = reference;
        ArrivalScreeningId = arrivalScreeningId;
        DispatchNoteId = dispatchNoteId;
        BatchDate = DateOnly.FromDateTime(createdAtLocal);
        CreatedAtUtc = createdAtUtc.UtcDateTime;
    }

    public Guid Id { get; private set; }

    /// <summary>Unique batch reference of the form WR-YYYYMMDD-NN.</summary>
    public string Reference { get; private set; }

    public Guid ArrivalScreeningId { get; private set; }

    public Guid DispatchNoteId { get; private set; }

    public DispatchNote? DispatchNote { get; private set; }

    /// <summary>Local date the batch was created; the date segment of <see cref="Reference"/>.</summary>
    public DateOnly BatchDate { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    internal static Batch Create(
        Guid id,
        string reference,
        Guid arrivalScreeningId,
        Guid dispatchNoteId,
        DateTime createdAtLocal,
        DateTimeOffset createdAtUtc) =>
        new(id, reference, arrivalScreeningId, dispatchNoteId, createdAtLocal, createdAtUtc);
}
