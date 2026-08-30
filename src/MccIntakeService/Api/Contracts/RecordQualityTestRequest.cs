using System.ComponentModel.DataAnnotations;
using MccIntakeService.Application.QualityTests;
using MccIntakeService.Domain.QualityTests;
using Wonrich.QualityPanel;

namespace MccIntakeService.Api.Contracts;

/// <summary>The quality test panel an intake officer submits for a composite sample (SCRUM-7).</summary>
public sealed class RecordQualityTestRequest : IValidatableObject
{
    /// <summary>Fat percentage from the butyrometer.</summary>
    /// <example>4.1</example>
    [Range(0, 15, ErrorMessage = "Fat must be between 0 and 15 percent.")]
    public decimal FatPercent { get; set; }

    /// <summary>Lactometer reading as taken, before temperature correction.</summary>
    /// <example>28.5</example>
    [Range(0, 40, ErrorMessage = "The lactometer reading must be between 0 and 40.")]
    public decimal RawLactometerReading { get; set; }

    /// <summary>Milk temperature when the lactometer was read, in °C.</summary>
    /// <example>29.0</example>
    [Range(0, 50, ErrorMessage = "The reading temperature must be between 0 and 50 °C.")]
    public decimal TemperatureCelsius { get; set; }

    /// <summary>Added water detected in the sample.</summary>
    /// <example>0.0</example>
    [Range(0, 100, ErrorMessage = "Added water must be between 0 and 100 percent.")]
    public decimal WaterPercent { get; set; }

    /// <summary>
    /// The sample smelled as fresh milk should. Defaults to true: the officer confirms what is
    /// wrong, not what is right, and a client that says nothing found nothing wrong.
    /// </summary>
    /// <example>true</example>
    public bool SmellOk { get; set; } = true;

    /// <summary>The sample was the colour fresh milk should be.</summary>
    /// <example>true</example>
    public bool ColourOk { get; set; } = true;

    /// <summary>The sample tasted as fresh milk should.</summary>
    /// <example>true</example>
    public bool TasteOk { get; set; } = true;

    /// <summary>Shade the KQ dye settled at, from the seven-shade card.</summary>
    /// <example>Blue</example>
    public KqColour KqColour { get; set; }

    /// <summary>
    /// The alcohol cascade outcomes, keyed by stage. Supply 80% first; a stage is only required
    /// when the previous one came back positive, because the cascade stops at the first negative.
    /// </summary>
    [Required(ErrorMessage = "The alcohol cascade outcomes are required.")]
    [MinLength(1, ErrorMessage = "At least the 80% alcohol result is required.")]
    public Dictionary<AlcoholStage, StageOutcome> AlcoholOutcomes { get; set; } = [];

    /// <summary>Accept or Reject.</summary>
    /// <example>Accept</example>
    public TestVerdict Verdict { get; set; }

    /// <summary>The parameter that failed. Required when rejecting.</summary>
    /// <example>Snf</example>
    [StringLength(50)]
    public string? FailedParameter { get; set; }

    /// <summary>The failing parameter's recorded value. Required when rejecting.</summary>
    /// <example>7.90</example>
    [StringLength(50)]
    public string? FailedValue { get; set; }

    /// <summary>
    /// A rejection has to say what failed. Checked here so the officer gets a field-level
    /// validation error rather than a domain rule surfacing as a generic 400.
    /// </summary>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Verdict != TestVerdict.Reject)
        {
            yield break;
        }

        if (string.IsNullOrWhiteSpace(FailedParameter))
        {
            yield return new ValidationResult(
                "A rejection must name the parameter that failed.", [nameof(FailedParameter)]);
        }

        if (string.IsNullOrWhiteSpace(FailedValue))
        {
            yield return new ValidationResult(
                "A rejection must record the failing value.", [nameof(FailedValue)]);
        }
    }

    /// <summary>Maps the request onto the application command.</summary>
    public RecordTestCommand ToCommand(TestVerdict verdict, string? testedBy) => new(
        FatPercent,
        RawLactometerReading,
        TemperatureCelsius,
        WaterPercent,
        KqColour,
        AlcoholOutcomes,
        verdict,
        new SensoryCheck(SmellOk, ColourOk, TasteOk),
        FailedParameter,
        FailedValue,
        testedBy);
}
