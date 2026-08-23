using MccIntakeService.Domain.Common;
using MccIntakeService.Domain.Societies;

namespace MccIntakeService.Domain.Consignments;

/// <summary>
/// A delivery of raw milk from one society arriving at the chilling centre, recorded at the gate
/// before any quality testing takes place (SCRUM-6). This is the aggregate root: cans only exist
/// as part of a consignment, and the total quantity is always derived from them.
/// </summary>
public class Consignment
{
    private readonly List<ConsignmentCan> _cans = [];

    /// <summary>EF Core materialisation constructor.</summary>
    private Consignment()
    {
        Reference = string.Empty;
    }

    private Consignment(
        Guid id,
        string reference,
        Society society,
        DateTime arrivalAtLocal,
        IEnumerable<ConsignmentCan> cans,
        DateTimeOffset registeredAtUtc,
        string? registeredBy)
    {
        Id = id;
        Reference = reference;
        SocietyId = society.Id;
        Society = society;
        ArrivalAtLocal = arrivalAtLocal;
        ArrivalDate = DateOnly.FromDateTime(arrivalAtLocal);
        Status = ConsignmentStatus.Registered;
        RegisteredAtUtc = registeredAtUtc.UtcDateTime;
        RegisteredBy = registeredBy;

        _cans.AddRange(cans);
        TotalQuantityLitres = decimal.Round(_cans.Sum(can => can.QuantityLitres), 2, MidpointRounding.AwayFromZero);
    }

    public Guid Id { get; private set; }

    /// <summary>Unique human-facing reference of the form MCC-YYYYMMDD-SOCIETY-NN.</summary>
    public string Reference { get; private set; }

    public Guid SocietyId { get; private set; }

    public Society? Society { get; private set; }

    /// <summary>Wall-clock arrival time at the centre, in the centre's own time zone.</summary>
    public DateTime ArrivalAtLocal { get; private set; }

    /// <summary>Local intake date the consignment belongs to; the date segment of <see cref="Reference"/>.</summary>
    public DateOnly ArrivalDate { get; private set; }

    public ConsignmentStatus Status { get; private set; }

    /// <summary>Sum of the quantities of every can, maintained by the aggregate rather than supplied by the caller.</summary>
    public decimal TotalQuantityLitres { get; private set; }

    public DateTime RegisteredAtUtc { get; private set; }

    /// <summary>Identifier of the intake officer. Populated once authentication lands (SCRUM-34).</summary>
    public string? RegisteredBy { get; private set; }

    public IReadOnlyCollection<ConsignmentCan> Cans => _cans.AsReadOnly();

    /// <summary>
    /// Records a consignment at the gate, enforcing every registration rule in SCRUM-6.
    /// </summary>
    /// <param name="id">Identity for the new consignment.</param>
    /// <param name="reference">Reference produced for this society and arrival date.</param>
    /// <param name="society">The registered supplying society.</param>
    /// <param name="arrivalAtLocal">Arrival wall-clock time in the centre's time zone.</param>
    /// <param name="cans">At least one can entry.</param>
    /// <param name="dailyCutoff">Configured local time after which intake closes.</param>
    /// <param name="nowLocal">Current wall-clock time in the centre's time zone.</param>
    /// <param name="registeredAtUtc">Instant the registration was accepted.</param>
    /// <param name="registeredBy">Intake officer identifier, when available.</param>
    public static Consignment Register(
        Guid id,
        string reference,
        Society society,
        DateTime arrivalAtLocal,
        IReadOnlyCollection<CanEntry> cans,
        TimeOnly dailyCutoff,
        DateTime nowLocal,
        DateTimeOffset registeredAtUtc,
        string? registeredBy = null)
    {
        ArgumentNullException.ThrowIfNull(society);
        ArgumentNullException.ThrowIfNull(cans);

        if (string.IsNullOrWhiteSpace(reference))
        {
            throw new DomainValidationException("A consignment reference is required.");
        }

        if (!society.IsActive)
        {
            throw new DomainValidationException(
                $"Society '{society.Code}' is no longer active and cannot supply consignments.");
        }

        if (cans.Count == 0)
        {
            throw new DomainValidationException("A consignment must contain at least one can.");
        }

        var duplicateCanNumbers = cans
            .GroupBy(can => can.CanNumber)
            .Where(group => group.Count() > 1)
            .Select(group => $"{society.CanLabelPrefix} {group.Key:00}")
            .ToArray();

        if (duplicateCanNumbers.Length > 0)
        {
            throw new DomainValidationException(
                $"The same can was entered more than once: {string.Join(", ", duplicateCanNumbers)}.");
        }

        EnsureArrivalIsRegistrable(arrivalAtLocal, dailyCutoff, nowLocal);

        var canEntities = cans
            .OrderBy(can => can.CanNumber)
            .Select(can => new ConsignmentCan(Guid.NewGuid(), society.CanLabelPrefix, can.CanNumber, can.QuantityLitres))
            .ToList();

        return new Consignment(id, reference, society, arrivalAtLocal, canEntities, registeredAtUtc, registeredBy);
    }

    /// <summary>
    /// Validates the arrival time on its own, so callers can reject a late consignment before
    /// spending a round trip generating its reference. <see cref="Register"/> re-runs this check.
    /// </summary>
    public static void EnsureArrivalIsRegistrable(DateTime arrivalAtLocal, TimeOnly dailyCutoff, DateTime nowLocal)
    {
        // The officer may correct the captured arrival time before submitting, but only backwards:
        // a consignment cannot arrive in the future. A small skew allowance absorbs clock drift
        // between the handheld device and the server.
        if (arrivalAtLocal > nowLocal.AddMinutes(1))
        {
            throw new DomainValidationException(
                $"Arrival time {arrivalAtLocal:yyyy-MM-dd HH:mm} is in the future and cannot be recorded.");
        }

        var arrivalTimeOfDay = TimeOnly.FromDateTime(arrivalAtLocal);
        if (arrivalTimeOfDay > dailyCutoff)
        {
            throw new IntakeCutoffExceededException(dailyCutoff, arrivalTimeOfDay);
        }
    }
}
