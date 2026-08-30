using MccIntakeService.Domain.Common;
using MccIntakeService.Domain.Tanks;
using Wonrich.QualityPanel;

namespace MccIntakeService.Domain.Dispatch;

/// <summary>One tank a bowser was loaded from, and how much came out of it.</summary>
/// <param name="TankId">The source tank.</param>
/// <param name="QuantityLitres">Litres drawn from that tank.</param>
public sealed record DispatchDraw(Guid TankId, decimal QuantityLitres);

/// <summary>
/// A source tank as it stands when the note is recorded: the load it is holding now, what that
/// load still has in it, and when the first of it went in.
/// </summary>
/// <param name="Tank">The tank itself.</param>
/// <param name="AvailableLitres">Litres its current fill still holds, after anything already drawn.</param>
/// <param name="FilledFromLocal">Wall-clock time of the first pour of the current fill, if any.</param>
public sealed record TankFill(ChillingTank Tank, decimal AvailableLitres, DateTime? FilledFromLocal);

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
    /// Records a dispatch note and closes the fill of every tank it emptied, enforcing the rules
    /// in SCRUM-8.
    /// </summary>
    /// <param name="id">Identity for the note.</param>
    /// <param name="reference">Reference issued for the dispatch date.</param>
    /// <param name="bowserRegistration">Registration of the bowser being loaded.</param>
    /// <param name="driverName">Driver taking the load.</param>
    /// <param name="dispatchedAtLocal">Wall-clock dispatch time.</param>
    /// <param name="panel">Quality panel taken at loading.</param>
    /// <param name="draws">Per-tank quantities drawn.</param>
    /// <param name="tanks">The source tanks, with the volume their current fill still holds.</param>
    /// <param name="dispatchedBy">Identifier of the manager recording the note.</param>
    /// <param name="nowLocal">Current wall-clock time at the centre.</param>
    /// <param name="recordedAtUtc">Instant the note was submitted.</param>
    public static DispatchNote Record(
        Guid id,
        string reference,
        string bowserRegistration,
        string driverName,
        DateTime dispatchedAtLocal,
        DispatchPanel panel,
        IReadOnlyCollection<DispatchDraw> draws,
        IReadOnlyDictionary<Guid, TankFill> tanks,
        string? dispatchedBy,
        DateTime nowLocal,
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

        // The manager may correct the captured dispatch time, but only backwards: a bowser cannot
        // leave in the future. Left unbounded, a mistyped year would issue its reference under
        // that date on a record nothing can afterwards amend. The skew allowance matches the one
        // the gate applies to arrival times.
        if (dispatchedAtLocal > nowLocal.AddMinutes(1))
        {
            throw new DomainValidationException(
                $"Dispatch time {dispatchedAtLocal:yyyy-MM-dd HH:mm} is in the future and cannot be recorded.");
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

            // The other end of the same bound: milk cannot leave before it arrived. A year typed
            // as 2016 lands here rather than being filed under a date the tank was never filled on.
            if (source.FilledFromLocal is { } filledFrom && dispatchedAtLocal < filledFrom)
            {
                throw new DomainValidationException(
                    $"Tank {source.Tank.Code} was not filled until {filledFrom:yyyy-MM-dd HH:mm}, "
                    + $"so it cannot have been dispatched at {dispatchedAtLocal:yyyy-MM-dd HH:mm}.");
            }

            // A bowser cannot carry away more than the tank holds. Allowing it would put milk on
            // the dispatch note that never existed, and the factory would reconcile against it.
            if (draw.QuantityLitres > source.AvailableLitres)
            {
                throw new DomainValidationException(
                    $"Tank {source.Tank.Code} holds {source.AvailableLitres} L, "
                    + $"so {draw.QuantityLitres} L cannot be drawn from it.");
            }

            // The fill is stamped on the source so the note goes on resolving to the load that
            // actually left, however many times the tank is filled again afterwards.
            sources.Add(new DispatchSource(
                Guid.NewGuid(),
                draw.TankId,
                source.Tank.FillNumber,
                draw.QuantityLitres));
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

        // Closing is part of recording the note, not a separate step a caller could forget. It
        // follows the tank being emptied rather than the note being submitted: a draw that leaves
        // a balance behind has not finished the load, and sealing the tank there would strand the
        // milk still in it.
        foreach (var draw in draws)
        {
            var source = tanks[draw.TankId];

            if (source.AvailableLitres == draw.QuantityLitres)
            {
                source.Tank.CloseFill(recordedAtUtc);
            }
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

    internal DispatchSource(Guid id, Guid tankId, int fillNumber, decimal quantityLitres)
    {
        Id = id;
        TankId = tankId;
        FillNumber = fillNumber;
        QuantityLitres = decimal.Round(quantityLitres, 2, MidpointRounding.AwayFromZero);
    }

    public Guid Id { get; private set; }

    public Guid DispatchNoteId { get; private set; }

    public Guid TankId { get; private set; }

    public ChillingTank? Tank { get; private set; }

    /// <summary>The tank fill this draw came out of; the manifest resolves through it.</summary>
    public int FillNumber { get; private set; }

    public decimal QuantityLitres { get; private set; }
}
