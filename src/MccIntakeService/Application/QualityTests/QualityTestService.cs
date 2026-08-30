using MccIntakeService.Application.Abstractions;
using MccIntakeService.Domain.Common;
using MccIntakeService.Domain.QualityTests;
using MccIntakeService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Wonrich.QualityPanel;

namespace MccIntakeService.Application.QualityTests;

/// <summary>The readings an officer takes for a composite sample (SCRUM-7).</summary>
public sealed record RecordTestCommand(
    decimal FatPercent,
    decimal RawLactometerReading,
    decimal TemperatureCelsius,
    decimal WaterPercent,
    KqColour KqColour,
    IReadOnlyDictionary<AlcoholStage, StageOutcome> AlcoholOutcomes,
    TestVerdict Verdict,
    SensoryCheck? Sensory = null,
    string? FailedParameter = null,
    string? FailedValue = null,
    string? TestedBy = null)
{
    /// <summary>
    /// The senses, defaulting to a sound sample. An older client that does not send them is
    /// recorded as having found nothing wrong, which is what its silence meant.
    /// </summary>
    public SensoryCheck SensoryOrSound => Sensory ?? SensoryCheck.Sound;
}

/// <summary>One derived value or reading, and whether it sits outside its configured limit.</summary>
/// <param name="Measure">What was measured.</param>
/// <param name="Value">The value, as recorded or derived.</param>
/// <param name="IsOutsideThreshold">Whether the officer should see this highlighted.</param>
/// <param name="Detail">Why it is out of range, when it is.</param>
public sealed record MeasureView(string Measure, string Value, bool IsOutsideThreshold, string? Detail);

/// <summary>
/// What the officer is shown before submitting: the derived values and anything out of range.
/// </summary>
/// <param name="CorrectedClr">Lactometer reading corrected for temperature.</param>
/// <param name="Snf">Solids-not-fat.</param>
/// <param name="TotalSolids">Total solids.</param>
/// <param name="StabilityGrade">How the alcohol cascade graded the sample.</param>
/// <param name="PassedAlcoholAt">The stage the cascade halted at.</param>
/// <param name="ClotOnBoiling">Whether the sample clotted on boiling, which forces a rejection.</param>
/// <param name="Measures">Every measure, flagged when outside its limit.</param>
/// <param name="MeetsStandard">Whether nothing is out of range.</param>
public sealed record TestPreview(
    decimal CorrectedClr,
    decimal Snf,
    decimal TotalSolids,
    string StabilityGrade,
    string PassedAlcoholAt,
    bool ClotOnBoiling,
    IReadOnlyList<MeasureView> Measures,
    bool MeetsStandard);

/// <summary>A recorded test, as read back.</summary>
public sealed record QualityTestView(
    Guid Id,
    Guid ConsignmentId,
    string ConsignmentReference,
    decimal FatPercent,
    decimal RawLactometerReading,
    decimal TemperatureCelsius,
    decimal WaterPercent,
    string KqColour,
    bool SmellOk,
    bool ColourOk,
    bool TasteOk,
    decimal CorrectedClr,
    decimal Snf,
    decimal TotalSolids,
    string StabilityGrade,
    string PassedAlcoholAt,
    IReadOnlyList<AlcoholStageView> AlcoholStages,
    TestVerdict Verdict,
    string? FailedParameter,
    string? FailedValue,
    string? TestedBy,
    DateTime TestedAtUtc);

/// <summary>One cascade stage as recorded.</summary>
public sealed record AlcoholStageView(string Stage, string Outcome);

/// <summary>Gate quality testing for registered consignments (SCRUM-7).</summary>
public interface IQualityTestService
{
    /// <summary>
    /// Evaluates readings without recording anything, so the officer sees the derived values and
    /// any breach before committing to a verdict.
    /// </summary>
    TestPreview Preview(RecordTestCommand command);

    /// <summary>Records the panel and settles the consignment's verdict.</summary>
    Task<QualityTestView> RecordAsync(
        string reference,
        RecordTestCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>Reads back the test recorded against a consignment.</summary>
    Task<QualityTestView?> GetForConsignmentAsync(
        string reference,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IQualityTestService" />
public sealed class QualityTestService : IQualityTestService
{
    private readonly MccIntakeDbContext _dbContext;
    private readonly IQualityPanelEvaluator _panel;
    private readonly IIntakeClock _clock;

    public QualityTestService(
        MccIntakeDbContext dbContext,
        IQualityPanelEvaluator panel,
        IIntakeClock clock)
    {
        _dbContext = dbContext;
        _panel = panel;
        _clock = clock;
    }

    public TestPreview Preview(RecordTestCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        return ToPreview(ReadingsFrom(command), command.SensoryOrSound, Evaluate(command));
    }

    public async Task<QualityTestView> RecordAsync(
        string reference,
        RecordTestCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var consignment = await _dbContext.Consignments
            .FirstOrDefaultAsync(candidate => candidate.Reference == reference, cancellationToken)
            ?? throw new EntityNotFoundException("Consignment", reference);

        // A consignment is tested once. The domain refuses a second verdict, and the unique index
        // on ConsignmentId settles a race between two officers submitting at the same moment.
        if (await _dbContext.QualityTests.AnyAsync(test => test.ConsignmentId == consignment.Id, cancellationToken))
        {
            throw new DomainValidationException(
                $"Consignment {reference} has already been tested.");
        }

        var result = Evaluate(command);

        var test = QualityTest.Record(
            Guid.NewGuid(),
            consignment,
            ReadingsFrom(command),
            command.SensoryOrSound,
            result,
            command.Verdict,
            command.FailedParameter,
            command.FailedValue,
            command.TestedBy,
            _clock.UtcNow);

        _dbContext.QualityTests.Add(test);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return ToView(test, consignment.Reference);
    }

    public async Task<QualityTestView?> GetForConsignmentAsync(
        string reference,
        CancellationToken cancellationToken = default)
    {
        var test = await _dbContext.QualityTests
            .AsNoTracking()
            .Include(candidate => candidate.Consignment)
            .FirstOrDefaultAsync(
                candidate => candidate.Consignment!.Reference == reference,
                cancellationToken);

        return test is null ? null : ToView(test, test.Consignment!.Reference);
    }

    /// <summary>
    /// The single place readings are evaluated. Preview and record share it so the figures the
    /// officer was shown are exactly the ones stored.
    /// </summary>
    private PanelResult Evaluate(RecordTestCommand command) => _panel.Evaluate(ReadingsFrom(command));

    private static PanelReadings ReadingsFrom(RecordTestCommand command) => new(
        command.FatPercent,
        command.RawLactometerReading,
        command.TemperatureCelsius,
        command.WaterPercent,
        command.AlcoholOutcomes,
        command.KqColour);

    private static TestPreview ToPreview(
        PanelReadings readings,
        SensoryCheck sensory,
        PanelResult result)
    {
        var failuresByMeasure = result.Failures.ToDictionary(
            failure => failure.Measure,
            failure => failure.Detail,
            StringComparer.Ordinal);

        MeasureView Measure(string name, string value) => new(
            name,
            value,
            failuresByMeasure.ContainsKey(name),
            failuresByMeasure.GetValueOrDefault(name));

        static MeasureView Sense(string name, bool ok) => new(
            name,
            ok ? "OK" : "Not OK",
            !ok,
            ok ? null : $"The officer found the sample's {name.ToLowerInvariant()} was not right.");

        var measures = new List<MeasureView>
        {
            Measure("FatPercent", result.Composition.FatPercent.ToString("0.00")),
            Measure("CorrectedClr", result.Composition.CorrectedClr.ToString("0.00")),
            Measure("Snf", result.Composition.Snf.ToString("0.00")),
            Measure("WaterPercent", readings.WaterPercent.ToString("0.00")),
            Measure("Stability", result.Cascade.Grade.ToString()),
            Measure("KqColour", result.KqColour.ToString()),

            // A sense the officer found wrong is shown like any other failed measure, so it can be
            // named as the reason for a rejection. Sour milk is a reason to turn a delivery away
            // whatever the lactometer reads.
            Sense("Smell", sensory.SmellOk),
            Sense("Colour", sensory.ColourOk),
            Sense("Taste", sensory.TasteOk)
        };

        return new TestPreview(
            result.Composition.CorrectedClr,
            result.Composition.Snf,
            result.Composition.TotalSolids,
            result.Cascade.Grade.ToString(),
            result.Cascade.HaltedAt.ToString(),
            result.Cascade.IsCurdled,
            measures,
            result.Passed && sensory.Passed);
    }

    private static QualityTestView ToView(QualityTest test, string reference) => new(
        test.Id,
        test.ConsignmentId,
        reference,
        test.FatPercent,
        test.RawLactometerReading,
        test.TemperatureCelsius,
        test.WaterPercent,
        test.KqColour,
        test.SmellOk,
        test.ColourOk,
        test.TasteOk,
        test.CorrectedClr,
        test.Snf,
        test.TotalSolids,
        test.StabilityGrade,
        test.PassedAlcoholAt,
        test.AlcoholStages
            .OrderBy(stage => stage.Order)
            .Select(stage => new AlcoholStageView(stage.Stage, stage.Outcome))
            .ToList(),
        test.Verdict,
        test.FailedParameter,
        test.FailedValue,
        test.TestedBy,
        test.TestedAtUtc);
}
