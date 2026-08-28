using System.ComponentModel.DataAnnotations;

namespace Wonrich.QualityPanel.Configuration;

/// <summary>
/// The limits a panel is judged against, bound from the "QualityThresholds" configuration section
/// (SCRUM-50).
/// </summary>
/// <remarks>
/// These are configuration, not constants, because they are a commercial and seasonal decision
/// rather than a property of milk: the centre retunes them without a release. The calculations in
/// <see cref="MilkComposition"/> are the opposite — they are fixed formulae and stay in code.
/// </remarks>
public sealed class QualityThresholds
{
    public const string SectionName = "QualityThresholds";

    /// <summary>Lowest acceptable fat percentage.</summary>
    [Range(0, 15, ErrorMessage = "QualityThresholds:MinimumFatPercent must be between 0 and 15.")]
    public decimal MinimumFatPercent { get; set; } = 3.5m;

    /// <summary>Lowest acceptable solids-not-fat.</summary>
    [Range(0, 15, ErrorMessage = "QualityThresholds:MinimumSnf must be between 0 and 15.")]
    public decimal MinimumSnf { get; set; } = 8.5m;

    /// <summary>Lowest acceptable corrected CLR.</summary>
    [Range(0, 40, ErrorMessage = "QualityThresholds:MinimumCorrectedClr must be between 0 and 40.")]
    public decimal MinimumCorrectedClr { get; set; } = 26.0m;

    /// <summary>Highest acceptable temperature at the gate, in °C.</summary>
    [Range(0, 50, ErrorMessage = "QualityThresholds:MaximumTemperatureCelsius must be between 0 and 50.")]
    public decimal MaximumTemperatureCelsius { get; set; } = 10.0m;

    /// <summary>The weakest stability grade still accepted.</summary>
    public StabilityGrade WorstAcceptableStability { get; set; } = StabilityGrade.MarginallyStable;

    /// <summary>The furthest-reduced KQ shade still accepted.</summary>
    public KqColour WorstAcceptableKqColour { get; set; } = KqColour.Green;
}
