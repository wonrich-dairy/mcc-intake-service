using Wonrich.QualityPanel;

namespace Wonrich.QualityPanel.Tests;

/// <summary>Covers every branch of the alcohol cascade state machine (SCRUM-50).</summary>
public class AlcoholCascadeTests
{
    private static IReadOnlyDictionary<AlcoholStage, StageOutcome> Outcomes(
        StageOutcome at80,
        StageOutcome? at75 = null,
        StageOutcome? at68 = null,
        StageOutcome? atCob = null)
    {
        var outcomes = new Dictionary<AlcoholStage, StageOutcome> { [AlcoholStage.Alcohol80] = at80 };

        if (at75 is not null)
        {
            outcomes[AlcoholStage.Alcohol75] = at75.Value;
        }

        if (at68 is not null)
        {
            outcomes[AlcoholStage.Alcohol68] = at68.Value;
        }

        if (atCob is not null)
        {
            outcomes[AlcoholStage.ClotOnBoiling] = atCob.Value;
        }

        return outcomes;
    }

    [Fact]
    public void The_stages_run_strongest_first()
    {
        Assert.Equal(
            [AlcoholStage.Alcohol80, AlcoholStage.Alcohol75, AlcoholStage.Alcohol68, AlcoholStage.ClotOnBoiling],
            AlcoholCascade.Order);
    }

    [Fact]
    public void Negative_at_80_halts_immediately_and_grades_stable()
    {
        var result = AlcoholCascade.Replay(Outcomes(StageOutcome.Negative));

        Assert.Equal(StabilityGrade.Stable, result.Grade);
        Assert.Equal(AlcoholStage.Alcohol80, result.HaltedAt);
        Assert.Single(result.StagesRun);
        Assert.False(result.IsCurdled);
    }

    [Fact]
    public void Negative_at_75_halts_there_and_grades_marginally_stable()
    {
        var result = AlcoholCascade.Replay(Outcomes(StageOutcome.Positive, StageOutcome.Negative));

        Assert.Equal(StabilityGrade.MarginallyStable, result.Grade);
        Assert.Equal(AlcoholStage.Alcohol75, result.HaltedAt);
        Assert.Equal(2, result.StagesRun.Count);
    }

    [Fact]
    public void Negative_at_68_halts_there_and_grades_unstable()
    {
        var result = AlcoholCascade.Replay(
            Outcomes(StageOutcome.Positive, StageOutcome.Positive, StageOutcome.Negative));

        Assert.Equal(StabilityGrade.Unstable, result.Grade);
        Assert.Equal(AlcoholStage.Alcohol68, result.HaltedAt);
        Assert.Equal(3, result.StagesRun.Count);
    }

    [Fact]
    public void Surviving_the_boil_after_every_alcohol_stage_clotted_grades_severely_unstable()
    {
        var result = AlcoholCascade.Replay(Outcomes(
            StageOutcome.Positive, StageOutcome.Positive, StageOutcome.Positive, StageOutcome.Negative));

        Assert.Equal(StabilityGrade.SeverelyUnstable, result.Grade);
        Assert.Equal(AlcoholStage.ClotOnBoiling, result.HaltedAt);
        Assert.Equal(4, result.StagesRun.Count);
        Assert.False(result.IsCurdled);
    }

    [Fact]
    public void Clotting_on_boiling_grades_curdled()
    {
        var result = AlcoholCascade.Replay(Outcomes(
            StageOutcome.Positive, StageOutcome.Positive, StageOutcome.Positive, StageOutcome.Positive));

        Assert.Equal(StabilityGrade.Curdled, result.Grade);
        Assert.True(result.IsCurdled);
        Assert.Equal(4, result.StagesRun.Count);
    }

    [Fact]
    public void Stages_beyond_the_first_negative_are_never_run()
    {
        var performed = new List<AlcoholStage>();

        AlcoholCascade.Run(stage =>
        {
            performed.Add(stage);

            return stage == AlcoholStage.Alcohol75 ? StageOutcome.Negative : StageOutcome.Positive;
        });

        // The gentler stages are a foregone conclusion once one comes back negative.
        Assert.Equal([AlcoholStage.Alcohol80, AlcoholStage.Alcohol75], performed);
    }

    [Fact]
    public void Replay_ignores_outcomes_recorded_past_the_halt()
    {
        // A negative at 80 ends it; a contradictory 75 reading is not part of the cascade at all.
        var result = AlcoholCascade.Replay(Outcomes(StageOutcome.Negative, StageOutcome.Positive));

        Assert.Equal(StabilityGrade.Stable, result.Grade);
        Assert.Single(result.StagesRun);
    }

    [Fact]
    public void Replay_rejects_a_missing_outcome_for_a_stage_the_cascade_reaches()
    {
        // Positive at 80 means 75 must have been run; refusing beats inventing a result.
        var exception = Assert.Throws<ArgumentException>(
            () => AlcoholCascade.Replay(Outcomes(StageOutcome.Positive)));

        Assert.Contains("Alcohol75", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_stage_reading_is_recorded_in_order()
    {
        var result = AlcoholCascade.Replay(
            Outcomes(StageOutcome.Positive, StageOutcome.Positive, StageOutcome.Negative));

        Assert.Equal(
            [AlcoholStage.Alcohol80, AlcoholStage.Alcohol75, AlcoholStage.Alcohol68],
            result.StagesRun.Select(reading => reading.Stage));

        Assert.Equal(
            [StageOutcome.Positive, StageOutcome.Positive, StageOutcome.Negative],
            result.StagesRun.Select(reading => reading.Outcome));
    }

    [Fact]
    public void Run_rejects_a_null_delegate()
    {
        Assert.Throws<ArgumentNullException>(() => AlcoholCascade.Run(null!));
        Assert.Throws<ArgumentNullException>(() => AlcoholCascade.Replay(null!));
    }
}
