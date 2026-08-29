using MccIntakeService.Domain.Common;
using MccIntakeService.Domain.Tanks;
using Wonrich.QualityPanel;

namespace MccIntakeService.Domain.Dispatch;

/// <summary>One tank a bowser was loaded from, and how much came out of it.</summary>
/// <param name="TankId">The source tank.</param>
/// <param name="QuantityLitres">Litres drawn from that tank.</param>
public sealed record DispatchDraw(Guid TankId, decimal QuantityLitres);

/// <summary>
/// The quality panel taken as the bowser is loaded, so the factory receives a documented
/// handover rather than an unqualified tanker (SCRUM-8).
/// </summary>
/// <param name="FatPercent">Fat percentage of the loaded milk.</param>
/// <param name="Snf">Solids-not-fat.</param>
/// <param name="KqColour">KQ shade, from the shared seven-shade scale.</param>
/// <param name="StabilityGrade">How the alcohol cascade graded the load.</param>
/// <param name="TemperatureCelsius">Temperature at dispatch.</param>
/// <param name="Remarks">Anything the manager wants on the record.</param>
public sealed record DispatchPanel(
    decimal FatPercent,
    decimal Snf,
    KqColour KqColour,
    StabilityGrade StabilityGrade,
    decimal TemperatureCelsius,
    string? Remarks);

/// <summary>
/// A bowser dispatch note (SCRUM-8): which tanks a bowser was loaded from, how much came from
/// each, and the panel taken at loading.
/// </summary>
/// <remarks>
/// The note is written once and read thereafter. It is the handover document the factory
/// receives, so amending it after the bowser has left would restate a record the other end is
/// already working from.
/// </remarks>
public class DispatchNote
{
    public const int MaxBowserRegistrationLength = 20;
    public const int MaxDriverNameLength = 100;
    public const int MaxRemarksLength = 500;

    private readonly List<DispatchSource> _sources = [];

    /// <summary>EF Core materialisation constructor.</summary>
    private DispatchNote()
    {
        Reference = string.Empty;
        BowserRegistration = string.Empty;
        DriverName = string.Empty;
        KqColour = string.Empty;
        StabilityGrade = string.Empty;
    }

    private DispatchNote(
        Guid id,
        string reference,
        string bowserRegistration,
        string driverName,
        DateTime dispatchedAtLocal,
        DispatchPanel panel,
        IEnumerable<DispatchSource> sources,
        string? dispatchedBy,
        DateTimeOffset recordedAtUtc)
    {
        Id = id;
        Reference = reference;
        BowserRegistration = bowserRegistration;
        DriverName = driverName;
        DispatchedAtLocal = dispatchedAtLocal;
        DispatchDate = DateOnly.FromDateTime(dispatchedAtLocal);

        FatPercent = panel.FatPercent;
        Snf = panel.Snf;
        KqColour = panel.KqColour.ToString();
        StabilityGrade = panel.StabilityGrade.ToString();
        TemperatureCelsius = panel.TemperatureCelsius;
        Remarks = panel.Remarks;

        DispatchedBy = dispatchedBy;
        RecordedAtUtc = recordedAtUtc.UtcDateTime;

        _sources.AddRange(sources);
        TotalQuantityLitres = decimal.Round(_sources.Sum(source => source.QuantityLitres), 2, MidpointRounding.AwayFromZero);
    }

    public Guid Id { get; private set; }

    /// <summary>Unique reference of the form DN-YYYYMMDD-NN.</summary>
    public string Reference { get; private set; }

    public string BowserRegistration { get; private set; }

    public string DriverName { get; private set; }

    /// <summary>Wall-clock dispatch time at the centre.</summary>
    public DateTime DispatchedAtLocal { get; private set; }

    /// <summary>Local dispatch date; the date segment of <see cref="Reference"/>.</summary>
    public DateOnly DispatchDate { get; private set; }

    /// <summary>Total drawn, summed from the per-tank quantities rather than supplied.</summary>
    public decimal TotalQuantityLitres { get; private set; }

    public decimal FatPercent { get; private set; }

    public decimal Snf { get; private set; }

    /// <summary>Stored by name so the KQ scale can gain shades without renumbering.</summary>
    public string KqColour { get; private set; }

    public string StabilityGrade { get; private set; }

    public decimal TemperatureCelsius { get; private set; }

    public string? Remarks { get; private set; }

    public string? DispatchedBy { get; private set; }

    public DateTime RecordedAtUtc { get; private set; }

    /// <summary>The tanks this bowser drew from.</summary>
    public IReadOnlyCollection<DispatchSource> Sources => _sources.AsReadOnly();

    /// <summary>
    /// Records a dispatch note and closes every tank it drew from, enforcing the rules in SCRUM-8.
    /// </summary>
    /// <param name="id">Identity for the note.</param>
    /// <param name="reference">Reference issued for the dispatch date.</param>
    /// <param name="bowserRegistration">Registration of the bowser being loaded.</param>
    /// <param name="driverName">Driver taking the load.</param>
    /// <param name="dispatchedAtLocal">Wall-clock dispatch time.</param>
    /// <param name="panel">Quality panel taken at loading.</param>
    /// <param name="draws">Per-tank quantities drawn.</param>
    /// <param name="tanks">The source tanks, with their currently available volume.</param>
    /// <param name="dispatchedBy">Identifier of the manager recording the note.</param>
    /// <param name="recordedAtUtc">Instant the note was submitted.</param>
    public static DispatchNote Record(
        Guid id,
        string reference,
        string bowserRegistration,
        string driverName,
        DateTime dispatchedAtLocal,
        DispatchPanel panel,
        IReadOnlyCollection<DispatchDraw> draws,
        IReadOnlyDictionary<Guid, (ChillingTank Tank, decimal AvailableLitres)> tanks,
        string? dispatchedBy,
        DateTimeOffset recordedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(panel);
        ArgumentNullException.ThrowIfNull(draws);
        ArgumentNullException.ThrowIfNull(tanks);

        if (string.IsNullOrWhiteSpace(reference))
        {
            throw new DomainValidationException("A dispatch reference is required.");
        }

        if (draws.Count == 0)
        {
            throw new DomainValidationException("A dispatch note must draw from at least one tank.");
        }

        var duplicates = draws
            .GroupBy(draw => draw.TankId)
            .Where(group => group.Count() > 1)
            .ToArray();

        if (duplicates.Length > 0)
        {
            throw new DomainValidationException(
                "The same tank was listed more than once on the dispatch note.");
        }

        var sources = new List<DispatchSource>();

        foreach (var draw in draws)
        {
            if (!tanks.TryGetValue(draw.TankId, out var source))
            {
                throw new DomainValidationException($"Tank '{draw.TankId}' is not a known chilling tank.");
            }

            if (draw.QuantityLitres <= 0)
            {
                throw new DomainValidationException(
                    $"The quantity drawn from tank {source.Tank.Code} must be greater than zero.");
            }

            // A bowser cannot carry away more than the tank holds. Allowing it would put milk on
            // the dispatch note that never existed, and the factory would reconcile against it.
            if (draw.QuantityLitres > source.AvailableLitres)
            {
                throw new DomainValidationException(
                    $"Tank {source.Tank.Code} holds {source.AvailableLitres} L, "
                    + $"so {draw.QuantityLitres} L cannot be drawn from it.");
            }

            if (source.Tank.IsClosed)
            {
                throw new DomainValidationException(
                    $"Tank {source.Tank.Code} has already been dispatched and is closed.");
            }

            sources.Add(new DispatchSource(Guid.NewGuid(), draw.TankId, draw.QuantityLitres));
        }

        var note = new DispatchNote(
            id,
            reference,
            Require(bowserRegistration, MaxBowserRegistrationLength, "A bowser registration"),
            Require(driverName, MaxDriverNameLength, "A driver name"),
            dispatchedAtLocal,
            panel with { Remarks = TrimOptional(panel.Remarks, MaxRemarksLength, "Remarks") },
            sources,
            dispatchedBy,
            recordedAtUtc);

        // Closing the tanks is part of recording the note, not a separate step a caller could
        // forget: once milk has left for the factory, pouring more in would corrupt the manifest
        // the note resolves through.
        foreach (var draw in draws)
        {
            tanks[draw.TankId].Tank.CloseForDispatch();
        }

        return note;
    }

    private static string Require(string value, int maxLength, string label)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainValidationException($"{label} is required.");
        }

        var trimmed = value.Trim();

        return trimmed.Length > maxLength
            ? throw new DomainValidationException($"{label} cannot exceed {maxLength} characters.")
            : trimmed;
    }

    private static string? TrimOptional(string? value, int maxLength, string label)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();

        return trimmed.Length > maxLength
            ? throw new DomainValidationException($"{label} cannot exceed {maxLength} characters.")
            : trimmed;
    }
}

/// <summary>One tank on a dispatch note, and the quantity drawn from it.</summary>
public class DispatchSource
{
    /// <summary>EF Core materialisation constructor.</summary>
    private DispatchSource()
    {
    }

    internal DispatchSource(Guid id, Guid tankId, decimal quantityLitres)
    {
        Id = id;
        TankId = tankId;
        QuantityLitres = decimal.Round(quantityLitres, 2, MidpointRounding.AwayFromZero);
    }

    public Guid Id { get; private set; }

    public Guid DispatchNoteId { get; private set; }

    public Guid TankId { get; private set; }

    public ChillingTank? Tank { get; private set; }

    public decimal QuantityLitres { get; private set; }
}
