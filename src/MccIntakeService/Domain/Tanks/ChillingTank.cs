using MccIntakeService.Domain.Common;
using MccIntakeService.Domain.Consignments;

namespace MccIntakeService.Domain.Tanks;

/// <summary>Whether a tank is in service.</summary>
public enum TankStatus
{
    /// <summary>In service and available to receive milk.</summary>
    Active = 0,

    /// <summary>Out of service. It keeps its manifest, but nothing new may be poured into it.</summary>
    UnderMaintenance = 1
}

/// <summary>
/// A chilling tank at the centre (SCRUM-52).
/// </summary>
/// <remarks>
/// Tanks are never deleted, for the reason societies are not: a tank is named on every pour and
/// every dispatch note it ever carried, and removing the row would leave those records pointing at
/// nothing. Taking one out of service is what retiring a tank means.
/// </remarks>
public class ChillingTank
{
    public const int MaxCodeLength = 10;
    public const int MaxNameLength = 100;

    /// <summary>EF Core materialisation constructor.</summary>
    private ChillingTank()
    {
        Code = string.Empty;
        Name = string.Empty;
    }

    public ChillingTank(Guid id, string code, string name, decimal capacityLitres)
    {
        Id = id;
        Code = Require(code, MaxCodeLength, nameof(code)).ToUpperInvariant();
        Name = Require(name, MaxNameLength, nameof(name));
        CapacityLitres = EnsureCapacity(capacityLitres);
    }

    public Guid Id { get; private set; }

    /// <summary>Short tank code as painted on the plant, e.g. "T1".</summary>
    public string Code { get; private set; }

    public string Name { get; private set; }

    /// <summary>Working volume of the tank, in litres.</summary>
    public decimal CapacityLitres { get; private set; }

    /// <summary>
    /// The load currently in the tank. Closure is scoped to the fill rather than to the tank row
    /// (SCRUM-8): a dispatch closes the load that left, and the tank goes on to hold the next
    /// one. A tank that closed permanently would be usable exactly once.
    /// </summary>
    public int FillNumber { get; private set; } = 1;

    /// <summary>When the tank's last fill was closed by a dispatch, if one has been.</summary>
    public DateTime? LastClosedAtUtc { get; private set; }

    /// <summary>Whether the tank is in service.</summary>
    public TankStatus Status { get; private set; } = TankStatus.Active;

    /// <summary>Temperature readings taken against this tank, newest first when loaded.</summary>
    public ICollection<TankTemperatureReading> TemperatureReadings { get; private set; } =
        new List<TankTemperatureReading>();

    /// <summary>Renames the tank and restates its working volume.</summary>
    public void Describe(string name, decimal capacityLitres)
    {
        Name = Require(name, MaxNameLength, nameof(name));
        CapacityLitres = EnsureCapacity(capacityLitres);
    }

    /// <summary>
    /// Takes the tank out of service, or puts it back. A tank holding milk cannot be taken out:
    /// the load in it would have nowhere to be recorded against and no way to be dispatched.
    /// </summary>
    public void ChangeStatus(TankStatus status, decimal quantityInTankLitres)
    {
        if (status == TankStatus.UnderMaintenance && quantityInTankLitres > 0)
        {
            throw new DomainValidationException(
                $"Tank {Code} still holds {quantityInTankLitres:0.##} L. Dispatch the load before "
                + "taking the tank out of service.");
        }

        Status = status;
    }

    private static string Require(string value, int maxLength, string field)
    {
        var trimmed = value?.Trim() ?? string.Empty;

        if (trimmed.Length == 0)
        {
            throw new DomainValidationException($"A tank {field} is required.");
        }

        return trimmed.Length > maxLength
            ? throw new DomainValidationException($"A tank {field} cannot exceed {maxLength} characters.")
            : trimmed;
    }

    private static decimal EnsureCapacity(decimal capacityLitres) =>
        capacityLitres <= 0
            ? throw new DomainValidationException("A tank's capacity must be greater than zero.")
            : capacityLitres;

    /// <summary>
    /// Closes the fill a dispatch has just emptied and opens the next one. The pours already on
    /// the closed fill stay with it, which is what keeps the dispatch note reading the same way
    /// once the tank starts filling again.
    /// </summary>
    public void CloseFill(DateTimeOffset closedAtUtc)
    {
        FillNumber++;
        LastClosedAtUtc = closedAtUtc.UtcDateTime;
    }
}

/// <summary>
/// One accepted consignment poured into a tank (SCRUM-52). The pour is what ties a tank's
/// contents back to the societies that supplied it.
/// </summary>
/// <remarks>
/// The quantities are copied onto the pour rather than read back through the consignment. A tank
/// manifest is a record of what physically went in, and it has to keep reading the same way even
/// if the consignment's own figures are ever restated.
/// </remarks>
public class TankPour
{
    /// <summary>EF Core materialisation constructor.</summary>
    private TankPour()
    {
    }

    private TankPour(
        Guid id,
        ChillingTank tank,
        Consignment consignment,
        string? pouredBy,
        DateTimeOffset pouredAtUtc,
        DateTime pouredAtLocal)
    {
        Id = id;
        TankId = tank.Id;
        FillNumber = tank.FillNumber;
        ConsignmentId = consignment.Id;
        QuantityLitres = consignment.TotalQuantityLitres;
        QuantityKg = consignment.TotalQuantityKg;
        PouredBy = pouredBy;
        PouredAtUtc = pouredAtUtc.UtcDateTime;
        PourDate = DateOnly.FromDateTime(pouredAtLocal);
    }

    public Guid Id { get; private set; }

    public Guid TankId { get; private set; }

    public ChillingTank? Tank { get; private set; }

    /// <summary>
    /// The tank fill this pour joined. A dispatch note resolves its manifest through the fill it
    /// drew from, so milk poured in after a bowser has left belongs to the next load rather than
    /// to the one already gone.
    /// </summary>
    public int FillNumber { get; private set; }

    public Guid ConsignmentId { get; private set; }

    public Consignment? Consignment { get; private set; }

    /// <summary>Litres poured, copied from the consignment at the moment of the pour.</summary>
    public decimal QuantityLitres { get; private set; }

    /// <summary>Kilograms poured, copied from the consignment at the moment of the pour.</summary>
    public decimal QuantityKg { get; private set; }

    /// <summary>Identity of the intake officer who confirmed the pour.</summary>
    public string? PouredBy { get; private set; }

    public DateTime PouredAtUtc { get; private set; }

    /// <summary>
    /// Date of the pour at the centre, so a manifest can be filtered by day without date
    /// arithmetic. Bucketed on local time, as the cutoff and the gate reference are: on UTC, a
    /// pour made before 05:30 local would be filed under the previous day.
    /// </summary>
    public DateOnly PourDate { get; private set; }

    /// <summary>
    /// Records a consignment being poured into a tank, enforcing the rules in SCRUM-52.
    /// </summary>
    /// <param name="id">Identity for the pour.</param>
    /// <param name="tank">The tank receiving the milk.</param>
    /// <param name="consignment">The consignment being poured.</param>
    /// <param name="pouredBy">Intake officer identifier.</param>
    /// <param name="pouredAtUtc">Instant the pour was confirmed.</param>
    /// <param name="pouredAtLocal">The same instant as wall-clock time at the centre.</param>
    public static TankPour Pour(
        Guid id,
        ChillingTank tank,
        Consignment consignment,
        string? pouredBy,
        DateTimeOffset pouredAtUtc,
        DateTime pouredAtLocal)
    {
        ArgumentNullException.ThrowIfNull(tank);
        ArgumentNullException.ThrowIfNull(consignment);

        // Only milk that passed the gate goes in a tank. An untested consignment has no verdict
        // yet, and a rejected one was turned away — neither is pourable.
        if (tank.Status != TankStatus.Active)
        {
            throw new DomainValidationException(
                $"Tank {tank.Code} is out of service and cannot receive milk.");
        }

        if (consignment.Status != ConsignmentStatus.Accepted)
        {
            throw new DomainValidationException(
                consignment.Status == ConsignmentStatus.Registered
                    ? $"Consignment {consignment.Reference} has not been tested yet and cannot be poured."
                    : $"Consignment {consignment.Reference} was rejected at the gate and cannot be poured.");
        }

        return new TankPour(id, tank, consignment, pouredBy, pouredAtUtc, pouredAtLocal);
    }
}

/// <summary>
/// One temperature taken against a tank (SCRUM-52). Chilled milk is held at a temperature, and
/// the reading is evidence the cold chain held: it is recorded rather than derived, and never
/// changed once taken.
/// </summary>
public class TankTemperatureReading
{
    /// <summary>Coldest and warmest a reading may be before it is more likely a typo.</summary>
    public const decimal MinCelsius = -5m;
    public const decimal MaxCelsius = 40m;

    /// <summary>EF Core materialisation constructor.</summary>
    private TankTemperatureReading()
    {
    }

    private TankTemperatureReading(
        Guid id,
        ChillingTank tank,
        decimal celsius,
        string? recordedBy,
        DateTimeOffset recordedAtUtc,
        DateTime recordedAtLocal)
    {
        Id = id;
        TankId = tank.Id;
        FillNumber = tank.FillNumber;
        Celsius = celsius;
        RecordedBy = recordedBy;
        RecordedAtUtc = recordedAtUtc.UtcDateTime;
        ReadingDate = DateOnly.FromDateTime(recordedAtLocal);
    }

    public Guid Id { get; private set; }

    public Guid TankId { get; private set; }

    public ChillingTank? Tank { get; private set; }

    /// <summary>The fill the tank was on when the reading was taken.</summary>
    public int FillNumber { get; private set; }

    public decimal Celsius { get; private set; }

    /// <summary>Identity of whoever took the reading.</summary>
    public string? RecordedBy { get; private set; }

    public DateTime RecordedAtUtc { get; private set; }

    /// <summary>
    /// Date of the reading at the centre, so a day's readings can be pulled without date
    /// arithmetic. Bucketed on local time, as pours and gate references are.
    /// </summary>
    public DateOnly ReadingDate { get; private set; }

    /// <summary>Records a reading against a tank.</summary>
    public static TankTemperatureReading Record(
        Guid id,
        ChillingTank tank,
        decimal celsius,
        string? recordedBy,
        DateTimeOffset recordedAtUtc,
        DateTime recordedAtLocal)
    {
        ArgumentNullException.ThrowIfNull(tank);

        if (celsius < MinCelsius || celsius > MaxCelsius)
        {
            throw new DomainValidationException(
                $"A tank reading must be between {MinCelsius:0.#} and {MaxCelsius:0.#} °C.");
        }

        return new TankTemperatureReading(id, tank, celsius, recordedBy, recordedAtUtc, recordedAtLocal);
    }
}
