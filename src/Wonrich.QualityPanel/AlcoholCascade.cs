namespace Wonrich.QualityPanel;

/// <summary>One rung of the alcohol stability cascade, strongest first.</summary>
public enum AlcoholStage
{
    /// <summary>80% alcohol — the harshest challenge, run first.</summary>
    Alcohol80 = 0,

    /// <summary>75% alcohol.</summary>
    Alcohol75 = 1,

    /// <summary>68% alcohol.</summary>
    Alcohol68 = 2,

    /// <summary>Clot on boiling — the last resort when every alcohol stage has clotted.</summary>
    ClotOnBoiling = 3
}

/// <summary>How one stage of the cascade came out.</summary>
public enum StageOutcome
{
    /// <summary>The sample did not clot. The milk is stable at this strength.</summary>
    Negative = 0,

    /// <summary>The sample clotted. The milk is unstable at this strength.</summary>
    Positive = 1
}

/// <summary>What the cascade concluded about the sample's stability.</summary>
public enum StabilityGrade
{
    /// <summary>Negative at 80%: stable against the harshest challenge.</summary>
    Stable = 0,

    /// <summary>Clotted at 80% but negative at 75%.</summary>
    MarginallyStable = 1,

    /// <summary>Clotted at 75% but negative at 68%.</summary>
    Unstable = 2,

    /// <summary>Clotted at 68% but survived boiling.</summary>
    SeverelyUnstable = 3,

    /// <summary>Clotted on boiling. The milk is not fit to take in.</summary>
    Curdled = 4
}

/// <summary>The outcome of the cascade, with the stages that were actually run.</summary>
/// <param name="Grade">What the cascade concluded.</param>
/// <param name="StagesRun">Each stage performed, in order, with its outcome.</param>
public sealed record CascadeResult(StabilityGrade Grade, IReadOnlyList<StageReading> StagesRun)
{
    /// <summary>The stage the cascade stopped at.</summary>
    public AlcoholStage HaltedAt => StagesRun[^1].Stage;

    /// <summary>Whether the milk clotted on boiling, which is an outright rejection.</summary>
    public bool IsCurdled => Grade == StabilityGrade.Curdled;
}

/// <summary>One stage performed, and what it showed.</summary>
/// <param name="Stage">The stage run.</param>
/// <param name="Outcome">Whether the sample clotted.</param>
public sealed record StageReading(AlcoholStage Stage, StageOutcome Outcome);

/// <summary>
/// The alcohol stability cascade as a state machine (SCRUM-50): 80% → 75% → 68% → clot on
/// boiling, halting at the first negative.
/// </summary>
/// <remarks>
/// <para>
/// A negative result means the sample did not clot, so the milk is stable at that strength — and
/// since each rung is gentler than the one before, it would be stable at all of them. That is why
/// the first negative ends the cascade: every remaining stage is a foregone conclusion, and
/// running them wastes reagent and the officer's time at the gate.
/// </para>
/// <para>
/// A positive means it clotted, and the cascade steps down to a gentler challenge to find how far
/// the instability goes. Clotting all the way through to boiling is the worst case.
/// </para>
/// </remarks>
public static class AlcoholCascade
{
    /// <summary>The stages in the order they are performed.</summary>
    public static readonly IReadOnlyList<AlcoholStage> Order =
    [
        AlcoholStage.Alcohol80,
        AlcoholStage.Alcohol75,
        AlcoholStage.Alcohol68,
        AlcoholStage.ClotOnBoiling
    ];

    /// <summary>
    /// Runs the cascade, asking <paramref name="performStage"/> for each stage in turn and
    /// stopping at the first negative.
    /// </summary>
    /// <param name="performStage">Performs one stage and reports whether the sample clotted.</param>
    public static CascadeResult Run(Func<AlcoholStage, StageOutcome> performStage)
    {
        ArgumentNullException.ThrowIfNull(performStage);

        var readings = new List<StageReading>();

        foreach (var stage in Order)
        {
            var outcome = performStage(stage);
            readings.Add(new StageReading(stage, outcome));

            if (outcome == StageOutcome.Negative)
            {
                return new CascadeResult(GradeForNegativeAt(stage), readings);
            }
        }

        // Positive at every stage, boiling included.
        return new CascadeResult(StabilityGrade.Curdled, readings);
    }

    /// <summary>
    /// Replays the cascade over readings already taken, for a panel being recorded after the
    /// fact. Stages recorded beyond the first negative are ignored rather than trusted, because
    /// the cascade defines them as never having been run.
    /// </summary>
    /// <param name="outcomes">Outcomes by stage; a stage the cascade reaches must be present.</param>
    public static CascadeResult Replay(IReadOnlyDictionary<AlcoholStage, StageOutcome> outcomes)
    {
        ArgumentNullException.ThrowIfNull(outcomes);

        return Run(stage => outcomes.TryGetValue(stage, out var outcome)
            ? outcome
            : throw new ArgumentException(
                $"The cascade reached {stage} but no outcome was recorded for it.", nameof(outcomes)));
    }

    private static StabilityGrade GradeForNegativeAt(AlcoholStage stage) => stage switch
    {
        AlcoholStage.Alcohol80 => StabilityGrade.Stable,
        AlcoholStage.Alcohol75 => StabilityGrade.MarginallyStable,
        AlcoholStage.Alcohol68 => StabilityGrade.Unstable,
        AlcoholStage.ClotOnBoiling => StabilityGrade.SeverelyUnstable,
        _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, "Unknown cascade stage.")
    };
}
