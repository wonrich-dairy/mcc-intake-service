using MccIntakeService.Domain.Common;
using MccIntakeService.Domain.Consignments;

namespace MccIntakeService.Domain.Tanks;

/// <summary>
/// A chilling tank at the centre. The centre has three, fixed by the plant rather than managed
/// through the API (SCRUM-52).
/// </summary>
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
        Code = code.Trim().ToUpperInvariant();
        Name = name.Trim();
        CapacityLitres = capacityLitres;
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
