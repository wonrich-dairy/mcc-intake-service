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
    /// Whether the tank has been dispatched to the factory. A closed tank takes no further pours
    /// (SCRUM-8): milk added after the bowser left would corrupt the manifest the dispatch note
    /// resolves through.
    /// </summary>
    public bool IsClosed { get; private set; }

    /// <summary>When the tank was closed by a dispatch.</summary>
    public DateTime? ClosedAtUtc { get; private set; }

    /// <summary>Closes the tank as part of recording a dispatch note.</summary>
    public void CloseForDispatch(DateTimeOffset? closedAtUtc = null)
    {
        if (IsClosed)
        {
            return;
        }

        IsClosed = true;
        ClosedAtUtc = (closedAtUtc ?? DateTimeOffset.UtcNow).UtcDateTime;
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
        Guid tankId,
        Consignment consignment,
        string? pouredBy,
        DateTimeOffset pouredAtUtc)
    {
        Id = id;
        TankId = tankId;
        ConsignmentId = consignment.Id;
        QuantityLitres = consignment.TotalQuantityLitres;
        QuantityKg = consignment.TotalQuantityKg;
        PouredBy = pouredBy;
        PouredAtUtc = pouredAtUtc.UtcDateTime;
        PourDate = DateOnly.FromDateTime(pouredAtUtc.UtcDateTime);
    }

    public Guid Id { get; private set; }

    public Guid TankId { get; private set; }

    public ChillingTank? Tank { get; private set; }

    public Guid ConsignmentId { get; private set; }

    public Consignment? Consignment { get; private set; }

    /// <summary>Litres poured, copied from the consignment at the moment of the pour.</summary>
    public decimal QuantityLitres { get; private set; }

    /// <summary>Kilograms poured, copied from the consignment at the moment of the pour.</summary>
    public decimal QuantityKg { get; private set; }

    /// <summary>Identity of the intake officer who confirmed the pour.</summary>
    public string? PouredBy { get; private set; }

    public DateTime PouredAtUtc { get; private set; }

    /// <summary>Date of the pour, so a manifest can be filtered by day without date arithmetic.</summary>
    public DateOnly PourDate { get; private set; }

    /// <summary>
    /// Records a consignment being poured into a tank, enforcing the rules in SCRUM-52.
    /// </summary>
    /// <param name="id">Identity for the pour.</param>
    /// <param name="tank">The tank receiving the milk.</param>
    /// <param name="consignment">The consignment being poured.</param>
    /// <param name="pouredBy">Intake officer identifier.</param>
    /// <param name="pouredAtUtc">Instant the pour was confirmed.</param>
    public static TankPour Pour(
        Guid id,
        ChillingTank tank,
        Consignment consignment,
        string? pouredBy,
        DateTimeOffset pouredAtUtc)
    {
        ArgumentNullException.ThrowIfNull(tank);
        ArgumentNullException.ThrowIfNull(consignment);

        if (tank.IsClosed)
        {
            throw new DomainValidationException(
                $"Tank {tank.Code} has been dispatched and is closed to further pours.");
        }

        // Only milk that passed the gate goes in a tank. An untested consignment has no verdict
        // yet, and a rejected one was turned away — neither is pourable.
        if (consignment.Status != ConsignmentStatus.Accepted)
        {
            throw new DomainValidationException(
                consignment.Status == ConsignmentStatus.Registered
                    ? $"Consignment {consignment.Reference} has not been tested yet and cannot be poured."
                    : $"Consignment {consignment.Reference} was rejected at the gate and cannot be poured.");
        }

        return new TankPour(id, tank.Id, consignment, pouredBy, pouredAtUtc);
    }
}
