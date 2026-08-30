using MccIntakeService.Domain.Common;
using MccIntakeService.Domain.Consignments;
using Wonrich.QualityPanel;

namespace MccIntakeService.Domain.QualityTests;

/// <summary>The officer's decision on a tested consignment.</summary>
public enum TestVerdict
{
    /// <summary>The milk meets the standard and may be poured.</summary>
    Accept = 0,

    /// <summary>The milk is turned away.</summary>
    Reject = 1
}

/// <summary>
/// The quality test panel recorded for one consignment's composite sample at the gate (SCRUM-7).
/// </summary>
/// <remarks>
/// <para>
/// A test belongs to exactly one consignment and a consignment is tested once. Re-testing would
/// leave two verdicts against the same milk with nothing to say which one governed the decision
/// to pour it.
/// </para>
/// <para>
/// Once submitted the record never changes. It is the evidence behind accepting or rejecting a
/// delivery a society is paid for, so it is written once and read thereafter — corrections are a
/// new consignment, not an edit.
/// </para>
/// </remarks>
public class QualityTest
{
    private readonly List<AlcoholStageRecord> _alcoholStages = [];

    /// <summary>EF Core materialisation constructor.</summary>
    private QualityTest()
    {
        StabilityGrade = string.Empty;
        KqColour = string.Empty;
    }

    private QualityTest(
        Guid id,
        Guid consignmentId,
        PanelReadings readings,
        PanelResult result,
        TestVerdict verdict,
        string? failedParameter,
        string? failedValue,
        string? testedBy,
        DateTimeOffset testedAtUtc)
    {
        Id = id;
        ConsignmentId = consignmentId;

        FatPercent = readings.FatPercent;
        RawLactometerReading = readings.RawLactometerReading;
        TemperatureCelsius = readings.TemperatureCelsius;
        WaterPercent = readings.WaterPercent;
        KqColour = readings.KqColour.ToString();

        CorrectedClr = result.Composition.CorrectedClr;
        Snf = result.Composition.Snf;
        TotalSolids = result.Composition.TotalSolids;
        StabilityGrade = result.Cascade.Grade.ToString();
        PassedAlcoholAt = result.Cascade.HaltedAt.ToString();

        Verdict = verdict;
        FailedParameter = failedParameter;
        FailedValue = failedValue;
        TestedBy = testedBy;
        TestedAtUtc = testedAtUtc.UtcDateTime;

        _alcoholStages.AddRange(result.Cascade.StagesRun.Select((reading, order) =>
            new AlcoholStageRecord(Guid.NewGuid(), order, reading.Stage.ToString(), reading.Outcome.ToString())));
    }

    public Guid Id { get; private set; }

    public Guid ConsignmentId { get; private set; }

    public Consignment? Consignment { get; private set; }

    /// <summary>Fat percentage as read from the butyrometer.</summary>
    public decimal FatPercent { get; private set; }

    /// <summary>Lactometer reading as taken, before temperature correction.</summary>
    public decimal RawLactometerReading { get; private set; }

    /// <summary>Milk temperature when the lactometer was read.</summary>
    public decimal TemperatureCelsius { get; private set; }

    /// <summary>Added water detected in the sample.</summary>
    public decimal WaterPercent { get; private set; }

    /// <summary>KQ shade recorded, stored by name so the scale can gain shades without renumbering.</summary>
    public string KqColour { get; private set; }

    /// <summary>Lactometer reading corrected to the calibration temperature.</summary>
    public decimal CorrectedClr { get; private set; }

    public decimal Snf { get; private set; }

    public decimal TotalSolids { get; private set; }

    /// <summary>How the alcohol cascade graded the sample's stability.</summary>
    public string StabilityGrade { get; private set; }

    /// <summary>The stage the cascade halted at — the strength the sample passed at.</summary>
    public string PassedAlcoholAt { get; private set; }

    public TestVerdict Verdict { get; private set; }

    /// <summary>The parameter that failed, required on a rejection.</summary>
    public string? FailedParameter { get; private set; }

    /// <summary>The failing parameter's recorded value, required on a rejection.</summary>
    public string? FailedValue { get; private set; }

    /// <summary>Identity of the intake officer who submitted the panel.</summary>
    public string? TestedBy { get; private set; }

    public DateTime TestedAtUtc { get; private set; }

    /// <summary>Each cascade stage actually performed, in the order it was run.</summary>
    public IReadOnlyCollection<AlcoholStageRecord> AlcoholStages => _alcoholStages.AsReadOnly();

    /// <summary>
    /// Records the panel and the officer's verdict against a consignment, enforcing the rules in
    /// SCRUM-7.
    /// </summary>
    /// <param name="id">Identity for the new test.</param>
    /// <param name="consignment">The consignment being tested.</param>
    /// <param name="readings">The readings the officer took.</param>
    /// <param name="result">The evaluated panel, from the shared library.</param>
    /// <param name="verdict">Accept or Reject, as the officer decided.</param>
    /// <param name="failedParameter">Parameter that failed; required when rejecting.</param>
    /// <param name="failedValue">That parameter's value; required when rejecting.</param>
    /// <param name="testedBy">Intake officer identifier.</param>
    /// <param name="testedAtUtc">Instant the panel was submitted.</param>
    public static QualityTest Record(
        Guid id,
        Consignment consignment,
        PanelReadings readings,
        PanelResult result,
        TestVerdict verdict,
        string? failedParameter,
        string? failedValue,
        string? testedBy,
        DateTimeOffset testedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(consignment);
        ArgumentNullException.ThrowIfNull(readings);
        ArgumentNullException.ThrowIfNull(result);

        if (consignment.Status != ConsignmentStatus.Registered)
        {
            throw new DomainValidationException(
                $"Consignment {consignment.Reference} has already been tested and is {consignment.Status}.");
        }

        // Clotting on boiling is not a matter of judgement: the milk is already curdled, so the
        // officer is not offered a verdict to weigh here.
        if (result.Cascade.IsCurdled && verdict == TestVerdict.Accept)
        {
            throw new DomainValidationException(
                "The sample clotted on boiling, so this consignment cannot be accepted.");
        }

        if (verdict == TestVerdict.Reject)
        {
            if (string.IsNullOrWhiteSpace(failedParameter))
            {
                throw new DomainValidationException(
                    "A rejection must name the parameter that failed.");
            }

            if (string.IsNullOrWhiteSpace(failedValue))
            {
                throw new DomainValidationException(
                    $"A rejection must record the value of '{failedParameter}' that failed.");
            }
        }

        var test = new QualityTest(
            id,
            consignment.Id,
            readings,
            result,
            verdict,
            failedParameter?.Trim(),
            failedValue?.Trim(),
            testedBy,
            testedAtUtc);

        consignment.SettleGateVerdict(verdict);

        return test;
    }

    /// <summary>Whether a positive clot-on-boiling forced this verdict.</summary>
    public bool WasForcedByClotOnBoiling =>
        StabilityGrade == Wonrich.QualityPanel.StabilityGrade.Curdled.ToString();
}

/// <summary>One alcohol cascade stage as performed, kept in the order it was run.</summary>
public class AlcoholStageRecord
{
    /// <summary>EF Core materialisation constructor.</summary>
    private AlcoholStageRecord()
    {
        Stage = string.Empty;
        Outcome = string.Empty;
    }

    internal AlcoholStageRecord(Guid id, int order, string stage, string outcome)
    {
        Id = id;
        Order = order;
        Stage = stage;
        Outcome = outcome;
    }

    public Guid Id { get; private set; }

    public Guid QualityTestId { get; private set; }

    /// <summary>Position in the cascade, so the sequence survives storage.</summary>
    public int Order { get; private set; }

    /// <summary>The stage performed, e.g. <c>Alcohol80</c>.</summary>
    public string Stage { get; private set; }

    /// <summary>Whether the sample clotted at this stage.</summary>
    public string Outcome { get; private set; }
}
