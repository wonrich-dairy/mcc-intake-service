using MccIntakeService.Domain.QualityTests;
using Wonrich.QualityPanel;
using Wonrich.QualityPanel.Configuration;

namespace MccIntakeService.Application.Traceability;

/// <summary>How close one measure sat to the limit that would have rejected it.</summary>
/// <param name="Measure">What was measured.</param>
/// <param name="Value">The recorded value.</param>
/// <param name="Threshold">The limit it was judged against.</param>
/// <param name="Margin">
/// How much room it had, as a fraction of the scale. Zero means it sat exactly on the limit;
/// negative means it was past it.
/// </param>
public sealed record MeasureMargin(string Measure, string Value, string Threshold, decimal Margin);

/// <summary>
/// Scores how close a consignment's gate results sat to the rejection thresholds, so a QCO
/// chasing a bad batch can start with the most marginal supplier (SCRUM-12).
/// </summary>
/// <remarks>
/// <para>
/// Measures are on different scales — percentages, lactometer degrees, positions on a colour card
/// — so a raw distance cannot be compared across them. Each is converted to a fraction of its own
/// scale, and the tightest one becomes the consignment's score.
/// </para>
/// <para>
/// This is a triage aid, not a verdict. Every consignment scored here already passed the gate;
/// the ranking only says which passed by the narrowest room.
/// </para>
/// </remarks>
public static class MarginToThreshold
{
    /// <summary>Score used when a consignment has no gate results to judge.</summary>
    public const decimal Unknown = -1m;

    /// <summary>
    /// Computes the margin for each measure and returns them tightest first.
    /// </summary>
    public static IReadOnlyList<MeasureMargin> For(QualityTest test, QualityThresholds thresholds)
    {
        ArgumentNullException.ThrowIfNull(test);
        ArgumentNullException.ThrowIfNull(thresholds);

        var margins = new List<MeasureMargin>
        {
            AtLeast("FatPercent", test.FatPercent, thresholds.MinimumFatPercent),
            AtLeast("CorrectedClr", test.CorrectedClr, thresholds.MinimumCorrectedClr),
            AtLeast("Snf", test.Snf, thresholds.MinimumSnf),
            AtMost("WaterPercent", test.WaterPercent, thresholds.MaximumWaterPercent),
            OnScale(
                "KqColour",
                test.KqColour,
                thresholds.WorstAcceptableKqColour.ToString(),
                IndexOnScale(test.KqColour, KqColourScale.All.Select(colour => colour.ToString()).ToList()),
                IndexOnScale(
                    thresholds.WorstAcceptableKqColour.ToString(),
                    KqColourScale.All.Select(colour => colour.ToString()).ToList()),
                KqColourScale.All.Count - 1),
            OnScale(
                "Stability",
                test.StabilityGrade,
                thresholds.WorstAcceptableStability.ToString(),
                IndexOnScale(test.StabilityGrade, StabilityGrades),
                IndexOnScale(thresholds.WorstAcceptableStability.ToString(), StabilityGrades),
                StabilityGrades.Count - 1)
        };

        return margins.OrderBy(margin => margin.Margin).ToList();
    }

    /// <summary>The tightest margin across every measure — the consignment's ranking score.</summary>
    public static decimal Tightest(QualityTest test, QualityThresholds thresholds) =>
        For(test, thresholds).Min(margin => margin.Margin);

    private static readonly IReadOnlyList<string> StabilityGrades =
        Enum.GetValues<StabilityGrade>().Select(grade => grade.ToString()).ToList();

    /// <summary>
    /// A measure with a floor. Room is expressed relative to the floor, so a percentage and a
    /// lactometer reading can be compared.
    /// </summary>
    private static MeasureMargin AtLeast(string measure, decimal value, decimal minimum) => new(
        measure,
        value.ToString("0.00"),
        minimum.ToString("0.00"),
        minimum == 0 ? 1m : decimal.Round((value - minimum) / minimum, 4, MidpointRounding.AwayFromZero));

    /// <summary>
    /// A measure with a ceiling. Added water is the one threshold the panel judges from above:
    /// every other measure fails by being too low, this one by being too high. Room is expressed
    /// relative to the ceiling so it compares with the rest.
    /// </summary>
    private static MeasureMargin AtMost(string measure, decimal value, decimal maximum) => new(
        measure,
        value.ToString("0.00"),
        maximum.ToString("0.00"),
        // A ceiling of zero leaves no room by definition; anything above it was rejected at the
        // gate and never reaches this ranking, so the reading can only be sitting on the limit.
        maximum == 0 ? 0m : decimal.Round((maximum - value) / maximum, 4, MidpointRounding.AwayFromZero));

    /// <summary>
    /// A measure on an ordered scale. Room is the number of steps left before the worst
    /// acceptable value, as a fraction of the whole scale.
    /// </summary>
    private static MeasureMargin OnScale(
        string measure,
        string value,
        string worstAcceptable,
        int valueIndex,
        int worstIndex,
        int scaleSteps)
    {
        // An unrecognised name cannot be placed on the scale, so it is reported as unknown rather
        // than scored as if it sat at one end.
        if (valueIndex < 0 || worstIndex < 0 || scaleSteps <= 0)
        {
            return new MeasureMargin(measure, value, worstAcceptable, Unknown);
        }

        return new MeasureMargin(
            measure,
            value,
            worstAcceptable,
            decimal.Round((worstIndex - valueIndex) / (decimal)scaleSteps, 4, MidpointRounding.AwayFromZero));
    }

    private static int IndexOnScale(string value, IReadOnlyList<string> scale)
    {
        for (var index = 0; index < scale.Count; index++)
        {
            if (string.Equals(scale[index], value, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }
}
