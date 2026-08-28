using Microsoft.Extensions.Options;
using Wonrich.QualityPanel.Configuration;

namespace Wonrich.QualityPanel;

/// <summary>The readings taken for one sample.</summary>
/// <param name="FatPercent">Fat percentage from the butyrometer.</param>
/// <param name="RawLactometerReading">Lactometer reading as taken, before correction.</param>
/// <param name="TemperatureCelsius">Sample temperature when the lactometer was read.</param>
/// <param name="AlcoholOutcomes">Outcome of each cascade stage that was performed.</param>
/// <param name="KqColour">Shade the KQ dye settled at.</param>
public sealed record PanelReadings(
    decimal FatPercent,
    decimal RawLactometerReading,
    decimal TemperatureCelsius,
    IReadOnlyDictionary<AlcoholStage, StageOutcome> AlcoholOutcomes,
    KqColour KqColour);

/// <summary>One limit the sample failed.</summary>
/// <param name="Measure">What was measured, e.g. "Snf".</param>
/// <param name="Detail">Why it failed, naming the value and the limit.</param>
public sealed record PanelFailure(string Measure, string Detail);

/// <summary>The full outcome of a panel: what was derived, and whether it passes.</summary>
/// <param name="Composition">The derived composition.</param>
/// <param name="Cascade">How the alcohol cascade came out.</param>
/// <param name="KqColour">The KQ shade recorded.</param>
/// <param name="Failures">Every limit the sample missed. Empty means it passes.</param>
public sealed record PanelResult(
    CompositionResult Composition,
    CascadeResult Cascade,
    KqColour KqColour,
    IReadOnlyList<PanelFailure> Failures)
{
    /// <summary>Whether the sample met every configured limit.</summary>
    public bool Passed => Failures.Count == 0;
}

/// <summary>Evaluates a quality panel against the configured thresholds (SCRUM-50).</summary>
public interface IQualityPanelEvaluator
{
    /// <summary>Derives the composition, runs the cascade, and judges both against the limits.</summary>
    PanelResult Evaluate(PanelReadings readings);
}

/// <inheritdoc cref="IQualityPanelEvaluator" />
/// <remarks>
/// Both the MCC gate and the lab resolve this same type against the same configuration section,
/// which is what makes identical readings produce identical verdicts at either checkpoint.
/// </remarks>
public sealed class QualityPanelEvaluator : IQualityPanelEvaluator
{
    private readonly QualityThresholds _thresholds;

    public QualityPanelEvaluator(IOptions<QualityThresholds> thresholds)
    {
        _thresholds = thresholds.Value;
    }

    public PanelResult Evaluate(PanelReadings readings)
    {
        ArgumentNullException.ThrowIfNull(readings);

        var composition = MilkComposition.From(
            readings.FatPercent,
            readings.RawLactometerReading,
            readings.TemperatureCelsius);

        var cascade = AlcoholCascade.Replay(readings.AlcoholOutcomes);

        var failures = new List<PanelFailure>();

        if (composition.FatPercent < _thresholds.MinimumFatPercent)
        {
            failures.Add(new PanelFailure(
                nameof(composition.FatPercent),
                $"Fat is {composition.FatPercent}%, below the minimum {_thresholds.MinimumFatPercent}%."));
        }

        if (composition.CorrectedClr < _thresholds.MinimumCorrectedClr)
        {
            failures.Add(new PanelFailure(
                nameof(composition.CorrectedClr),
                $"Corrected CLR is {composition.CorrectedClr}, below the minimum {_thresholds.MinimumCorrectedClr}."));
        }

        if (composition.Snf < _thresholds.MinimumSnf)
        {
            failures.Add(new PanelFailure(
                nameof(composition.Snf),
                $"SNF is {composition.Snf}, below the minimum {_thresholds.MinimumSnf}."));
        }

        if (readings.TemperatureCelsius > _thresholds.MaximumTemperatureCelsius)
        {
            failures.Add(new PanelFailure(
                "Temperature",
                $"Temperature is {readings.TemperatureCelsius} °C, above the maximum "
                + $"{_thresholds.MaximumTemperatureCelsius} °C."));
        }

        // Grades run best to worst, so a grade beyond the configured worst-acceptable is a fail.
        if (cascade.Grade > _thresholds.WorstAcceptableStability)
        {
            failures.Add(new PanelFailure(
                "Stability",
                $"Alcohol cascade graded {cascade.Grade} at {cascade.HaltedAt}, beyond the "
                + $"acceptable {_thresholds.WorstAcceptableStability}."));
        }

        if (!KqColourScale.IsAtLeastAsGoodAs(readings.KqColour, _thresholds.WorstAcceptableKqColour))
        {
            failures.Add(new PanelFailure(
                "KqColour",
                $"KQ colour is {readings.KqColour}, beyond the acceptable "
                + $"{_thresholds.WorstAcceptableKqColour}."));
        }

        return new PanelResult(composition, cascade, readings.KqColour, failures);
    }
}
