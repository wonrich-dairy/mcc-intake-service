using MccIntakeService.Domain.QualityTests;
using MccIntakeService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Wonrich.QualityPanel.Configuration;

namespace MccIntakeService.Application.Traceability;

/// <summary>The gate results recorded for a consignment.</summary>
public sealed record TracedQualityTestView(
    decimal FatPercent,
    decimal RawLactometerReading,
    decimal TemperatureCelsius,
    decimal WaterPercent,
    string KqColour,
    decimal CorrectedClr,
    decimal Snf,
    decimal TotalSolids,
    string StabilityGrade,
    string PassedAlcoholAt,
    string Verdict,
    string? TestedBy,
    DateTime TestedAtUtc,
    IReadOnlyList<MeasureMargin> Margins);

/// <summary>One consignment that contributed to the batch.</summary>
/// <param name="Missing">
/// What could not be resolved for this consignment. Empty when everything upstream was present.
/// </param>
public sealed record TracedConsignmentView(
    string Reference,
    string SocietyCode,
    string SocietyName,
    IReadOnlyList<string> CanLabels,
    decimal QuantityLitres,
    decimal QuantityKg,
    DateTime ArrivalAtLocal,
    string? RegisteredBy,
    DateTime RegisteredAtUtc,
    DateTime PouredAtUtc,
    string? PouredBy,
    TracedQualityTestView? QualityTest,
    decimal TightestMargin,
    IReadOnlyList<string> Missing);

/// <summary>One tank the batch drew from.</summary>
public sealed record TracedTankView(
    string TankCode,
    string TankName,
    decimal QuantityDrawnLitres,
    IReadOnlyList<TracedConsignmentView> Consignments,
    IReadOnlyList<string> Missing);

/// <summary>A society's standing across everything it contributed to this batch.</summary>
public sealed record SocietyRiskView(
    string SocietyCode,
    string SocietyName,
    int ConsignmentCount,
    decimal TightestMargin,
    string? TightestMeasure);

/// <summary>A batch resolved back to everything that went into it (SCRUM-12).</summary>
public sealed record BatchTraceView(
    string BatchReference,
    DateOnly BatchDate,
    DateTime CreatedAtUtc,
    DateTime ArrivedAtLocal,
    string? ScreenedBy,
    DateTime ScreenedAtUtc,
    string DispatchNoteReference,
    string BowserRegistration,
    string DriverName,
    DateTime DispatchedAtLocal,
    string? DispatchedBy,
    DateTime DispatchRecordedAtUtc,
    decimal TotalDispatchedLitres,
    IReadOnlyList<TracedTankView> Tanks,
    IReadOnlyList<SocietyRiskView> SocietiesByMargin,
    IReadOnlyList<string> Missing);

/// <summary>Resolves a batch back to its source tanks and consignments (SCRUM-12).</summary>
public interface IBatchTraceService
{
    /// <summary>Traces one batch, or null when no batch carries that reference.</summary>
    Task<BatchTraceView?> TraceAsync(string batchReference, CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IBatchTraceService" />
public sealed class BatchTraceService : IBatchTraceService
{
    private readonly MccIntakeDbContext _dbContext;
    private readonly QualityThresholds _thresholds;

    public BatchTraceService(MccIntakeDbContext dbContext, IOptions<QualityThresholds> thresholds)
    {
        _dbContext = dbContext;
        _thresholds = thresholds.Value;
    }

    public async Task<BatchTraceView?> TraceAsync(
        string batchReference,
        CancellationToken cancellationToken = default)
    {
        var screening = await _dbContext.ArrivalScreenings
            .AsNoTracking()
            .Include(candidate => candidate.Batch)
            .Include(candidate => candidate.DispatchNote)!
                .ThenInclude(note => note!.Sources)
                    .ThenInclude(source => source.Tank)
            .FirstOrDefaultAsync(
                candidate => candidate.Batch != null && candidate.Batch.Reference == batchReference,
                cancellationToken);

        if (screening?.Batch is null)
        {
            return null;
        }

        var missing = new List<string>();
        var note = screening.DispatchNote;

        if (note is null)
        {
            // Recorded explicitly rather than returning a half-populated trace that reads as
            // though the batch simply had no source.
            missing.Add("The dispatch note this batch came from could not be resolved.");

            return new BatchTraceView(
                screening.Batch.Reference,
                screening.Batch.BatchDate,
                screening.Batch.CreatedAtUtc,
                screening.ArrivedAtLocal,
                screening.ScreenedBy,
                screening.ScreenedAtUtc,
                string.Empty, string.Empty, string.Empty,
                default, null, default, 0m,
                [],
                [],
                missing);
        }

        var tanks = new List<TracedTankView>();

        foreach (var source in note.Sources.OrderBy(source => source.Tank?.Code))
        {
            tanks.Add(await TraceTankAsync(source.TankId, source.FillNumber, source.Tank?.Code,
                source.Tank?.Name, source.QuantityLitres, cancellationToken));
        }

        if (tanks.Count == 0)
        {
            missing.Add("The dispatch note lists no source tanks.");
        }

        return new BatchTraceView(
            screening.Batch.Reference,
            screening.Batch.BatchDate,
            screening.Batch.CreatedAtUtc,
            screening.ArrivedAtLocal,
            screening.ScreenedBy,
            screening.ScreenedAtUtc,
            note.Reference,
            note.BowserRegistration,
            note.DriverName,
            note.DispatchedAtLocal,
            note.DispatchedBy,
            note.RecordedAtUtc,
            note.TotalQuantityLitres,
            tanks,
            RankSocieties(tanks),
            missing);
    }

    private async Task<TracedTankView> TraceTankAsync(
        Guid tankId,
        int fillNumber,
        string? code,
        string? name,
        decimal drawn,
        CancellationToken cancellationToken)
    {
        var missing = new List<string>();

        if (code is null)
        {
            missing.Add("The tank this quantity was drawn from could not be resolved.");
        }

        // Scoped to the fill the bowser actually drew from (SCRUM-8). Reading the whole tank
        // would pull in every load it has held since, so a batch would trace back to societies
        // whose milk was never in it — and their risk standing would be scored on it.
        var pours = await _dbContext.TankPours
            .AsNoTracking()
            .Where(pour => pour.TankId == tankId && pour.FillNumber == fillNumber)
            .Include(pour => pour.Consignment)!
                .ThenInclude(consignment => consignment!.Society)
            .Include(pour => pour.Consignment)!
                .ThenInclude(consignment => consignment!.Cans)
            .OrderBy(pour => pour.PouredAtUtc)
            .ToListAsync(cancellationToken);

        if (pours.Count == 0)
        {
            missing.Add("No pours are recorded against the load this tank was drawn from.");
        }

        var consignments = new List<TracedConsignmentView>();

        foreach (var pour in pours)
        {
            var consignment = pour.Consignment;

            if (consignment is null)
            {
                missing.Add($"A pour of {pour.QuantityLitres} L has no consignment behind it.");

                continue;
            }

            var test = await _dbContext.QualityTests
                .AsNoTracking()
                .Include(candidate => candidate.AlcoholStages)
                .FirstOrDefaultAsync(candidate => candidate.ConsignmentId == consignment.Id, cancellationToken);

            var perConsignmentMissing = new List<string>();

            if (consignment.Society is null)
            {
                perConsignmentMissing.Add("The supplying society could not be resolved.");
            }

            if (test is null)
            {
                // A consignment in a tank should always carry a gate verdict. Saying so beats
                // rendering blank fields the QCO might read as "all clear".
                perConsignmentMissing.Add("No gate quality test is recorded for this consignment.");
            }

            consignments.Add(new TracedConsignmentView(
                consignment.Reference,
                consignment.Society?.Code ?? string.Empty,
                consignment.Society?.Name ?? string.Empty,
                consignment.Cans.OrderBy(can => can.CanNumber).Select(can => can.CanLabel).ToList(),
                pour.QuantityLitres,
                pour.QuantityKg,
                consignment.ArrivalAtLocal,
                consignment.RegisteredBy,
                consignment.RegisteredAtUtc,
                pour.PouredAtUtc,
                pour.PouredBy,
                test is null ? null : ToTestView(test),
                test is null ? MarginToThreshold.Unknown : MarginToThreshold.Tightest(test, _thresholds),
                perConsignmentMissing));
        }

        return new TracedTankView(code ?? string.Empty, name ?? string.Empty, drawn, consignments, missing);
    }

    private TracedQualityTestView ToTestView(QualityTest test) => new(
        test.FatPercent,
        test.RawLactometerReading,
        test.TemperatureCelsius,
        test.WaterPercent,
        test.KqColour,
        test.CorrectedClr,
        test.Snf,
        test.TotalSolids,
        test.StabilityGrade,
        test.PassedAlcoholAt,
        test.Verdict.ToString(),
        test.TestedBy,
        test.TestedAtUtc,
        MarginToThreshold.For(test, _thresholds));

    /// <summary>
    /// Ranks societies most marginal first, so the QCO starts with the supplier whose milk passed
    /// by the narrowest room. A society with no gate results sorts last rather than first: it is
    /// unknown, not safe, and the explicit missing entries are what flag it.
    /// </summary>
    private static IReadOnlyList<SocietyRiskView> RankSocieties(IReadOnlyList<TracedTankView> tanks)
    {
        return tanks
            .SelectMany(tank => tank.Consignments)
            .Where(consignment => !string.IsNullOrEmpty(consignment.SocietyCode))
            .GroupBy(consignment => (consignment.SocietyCode, consignment.SocietyName))
            .Select(group =>
            {
                var scored = group
                    .Where(consignment => consignment.QualityTest is not null)
                    .ToList();

                var tightest = scored.Count == 0
                    ? MarginToThreshold.Unknown
                    : scored.Min(consignment => consignment.TightestMargin);

                var measure = scored
                    .SelectMany(consignment => consignment.QualityTest!.Margins)
                    .Where(margin => margin.Margin == tightest)
                    .Select(margin => margin.Measure)
                    .FirstOrDefault();

                return new SocietyRiskView(
                    group.Key.SocietyCode,
                    group.Key.SocietyName,
                    group.Count(),
                    tightest,
                    measure);
            })
            .OrderBy(society => society.TightestMargin == MarginToThreshold.Unknown ? 1 : 0)
            .ThenBy(society => society.TightestMargin)
            .ThenBy(society => society.SocietyCode, StringComparer.Ordinal)
            .ToList();
    }
}
