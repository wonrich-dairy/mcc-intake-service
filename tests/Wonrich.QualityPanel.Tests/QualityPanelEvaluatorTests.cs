using Microsoft.Extensions.Options;
using Wonrich.QualityPanel;
using Wonrich.QualityPanel.Configuration;

namespace Wonrich.QualityPanel.Tests;

/// <summary>Covers the KQ scale and threshold evaluation against configuration (SCRUM-50).</summary>
public class QualityPanelEvaluatorTests
{
    private static QualityPanelEvaluator Evaluator(QualityThresholds? thresholds = null) =>
        new(Options.Create(thresholds ?? new QualityThresholds()));

    /// <summary>Readings that comfortably pass the default thresholds.</summary>
    private static PanelReadings GoodSample(
        decimal fat = 4.0m,
        decimal raw = 28.0m,
        decimal temperature = 28.0m,
        KqColour kq = KqColour.Blue,
        StageOutcome at80 = StageOutcome.Negative,
        decimal water = 0m) =>
        new(fat, raw, temperature, water, new Dictionary<AlcoholStage, StageOutcome>
        {
            [AlcoholStage.Alcohol80] = at80,
            [AlcoholStage.Alcohol75] = StageOutcome.Negative
        }, kq);

    [Fact]
    public void A_sound_sample_passes_with_no_failures()
    {
        var result = Evaluator().Evaluate(GoodSample());

        Assert.True(result.Passed);
        Assert.Empty(result.Failures);
        Assert.Equal(StabilityGrade.Stable, result.Cascade.Grade);
    }

    [Fact]
    public void Fat_below_the_configured_minimum_fails()
    {
        var result = Evaluator().Evaluate(GoodSample(fat: 3.0m));

        Assert.False(result.Passed);
        Assert.Contains(result.Failures, failure => failure.Measure == "FatPercent");
    }

    [Fact]
    public void Snf_below_the_configured_minimum_fails()
    {
        // Low fat and a low lactometer reading together drag SNF under 8.5.
        var result = Evaluator().Evaluate(GoodSample(fat: 2.0m, raw: 24.0m));

        Assert.Contains(result.Failures, failure => failure.Measure == "Snf");
    }

    [Fact]
    public void A_corrected_clr_below_the_minimum_fails()
    {
        var result = Evaluator().Evaluate(GoodSample(raw: 24.0m));

        Assert.Contains(result.Failures, failure => failure.Measure == "CorrectedClr");
    }

    [Fact]
    public void Stability_beyond_the_acceptable_grade_fails()
    {
        var readings = new PanelReadings(4.0m, 28.0m, 28.0m, 0m, new Dictionary<AlcoholStage, StageOutcome>
        {
            [AlcoholStage.Alcohol80] = StageOutcome.Positive,
            [AlcoholStage.Alcohol75] = StageOutcome.Positive,
            [AlcoholStage.Alcohol68] = StageOutcome.Negative
        }, KqColour.Blue);

        var result = Evaluator().Evaluate(readings);

        // Default worst-acceptable is MarginallyStable; Unstable is a rung beyond it.
        Assert.Equal(StabilityGrade.Unstable, result.Cascade.Grade);
        Assert.Contains(result.Failures, failure => failure.Measure == "Stability");
    }

    [Fact]
    public void Added_water_beyond_the_configured_maximum_fails()
    {
        var result = Evaluator().Evaluate(GoodSample(water: 5.0m));

        Assert.Contains(result.Failures, failure => failure.Measure == "WaterPercent");
    }

    [Fact]
    public void A_kq_colour_beyond_the_acceptable_shade_fails()
    {
        var result = Evaluator().Evaluate(GoodSample(kq: KqColour.Pink));

        Assert.Contains(result.Failures, failure => failure.Measure == "KqColour");
    }

    [Fact]
    public void The_worst_acceptable_kq_shade_itself_still_passes()
    {
        var result = Evaluator().Evaluate(GoodSample(kq: KqColour.Purple));

        Assert.True(result.Passed);
    }

    [Fact]
    public void Every_failed_measure_is_reported_not_just_the_first()
    {
        var result = Evaluator().Evaluate(GoodSample(fat: 1.0m, raw: 20.0m, temperature: 27.0m, kq: KqColour.White));

        // The officer should see everything wrong in one pass, not fix and resubmit repeatedly.
        Assert.Equal(4, result.Failures.Count);
    }

    [Fact]
    public void Thresholds_come_from_configuration_rather_than_constants()
    {
        var lenient = new QualityThresholds { MinimumFatPercent = 1.0m, MinimumSnf = 1.0m, MinimumCorrectedClr = 1.0m };
        var strict = new QualityThresholds { MinimumFatPercent = 9.0m };

        var sample = GoodSample(fat: 3.0m);

        Assert.True(Evaluator(lenient).Evaluate(sample).Passed);
        Assert.False(Evaluator(strict).Evaluate(sample).Passed);
    }

    [Fact]
    public void The_same_readings_give_the_same_verdict_from_two_separately_configured_consumers()
    {
        // Stands in for the MCC service and the Lab service each resolving their own evaluator:
        // same library, same configuration, therefore the same answer at either checkpoint.
        var mcc = Evaluator();
        var lab = Evaluator();

        var readings = GoodSample(fat: 3.6m, raw: 27.2m, temperature: 26.0m, kq: KqColour.Purple);

        var atGate = mcc.Evaluate(readings);
        var atLab = lab.Evaluate(readings);

        Assert.Equal(atGate.Composition, atLab.Composition);
        Assert.Equal(atGate.Cascade.Grade, atLab.Cascade.Grade);
        Assert.Equal(atGate.Passed, atLab.Passed);
        Assert.Equal(
            atGate.Failures.Select(failure => failure.Measure),
            atLab.Failures.Select(failure => failure.Measure));
    }

    [Fact]
    public void The_evaluator_rejects_null_readings()
    {
        Assert.Throws<ArgumentNullException>(() => Evaluator().Evaluate(null!));
    }

    [Fact]
    public void The_kq_scale_runs_best_to_worst()
    {
        Assert.Equal(7, KqColourScale.All.Count);
        Assert.Equal(KqColour.Blue, KqColourScale.All[0]);
        Assert.Equal(KqColour.White, KqColourScale.All[^1]);

        Assert.True(KqColourScale.IsAtLeastAsGoodAs(KqColour.Blue, KqColour.Purple));
        Assert.True(KqColourScale.IsAtLeastAsGoodAs(KqColour.Purple, KqColour.Purple));
        Assert.False(KqColourScale.IsAtLeastAsGoodAs(KqColour.Pink, KqColour.Purple));

        Assert.True(KqColourScale.IsDefined(KqColour.Blue));
        Assert.False(KqColourScale.IsDefined((KqColour)99));
    }

    [Fact]
    public void The_kq_values_are_the_stored_contract()
    {
        // Renumbering these would silently reinterpret every panel already recorded.
        Assert.Equal(0, (int)KqColour.Blue);
        Assert.Equal(1, (int)KqColour.LightBlue);
        Assert.Equal(2, (int)KqColour.Purple);
        Assert.Equal(3, (int)KqColour.PurplePink);
        Assert.Equal(4, (int)KqColour.LightPink);
        Assert.Equal(5, (int)KqColour.Pink);
        Assert.Equal(6, (int)KqColour.White);
    }
}
