namespace MccIntakeService.Domain.QualityTests;

/// <summary>
/// What the officer's own senses say about the sample, before any instrument is involved (SCRUM-7).
/// </summary>
/// <param name="SmellOk">The sample smells as fresh milk should.</param>
/// <param name="ColourOk">The sample is the colour fresh milk should be.</param>
/// <param name="TasteOk">The sample tastes as fresh milk should.</param>
/// <remarks>
/// <para>
/// These are observations, not measurements: nothing here feeds the composition formulae or the
/// cascade, which is why they live in this service rather than in the shared quality panel
/// library. That library exists so the gate and the lab compute the same figures from the same
/// instrument readings; a nose is not an instrument.
/// </para>
/// <para>
/// A sense that is not right is treated as a failed measure, exactly like an out-of-range CLR: it
/// shows on the panel and can be named as the reason for a rejection. Sour milk is a reason to
/// turn a delivery away whatever the lactometer reads.
/// </para>
/// </remarks>
public sealed record SensoryCheck(bool SmellOk, bool ColourOk, bool TasteOk)
{
    /// <summary>A sample the officer found nothing wrong with, which is the usual case.</summary>
    public static SensoryCheck Sound { get; } = new(true, true, true);

    /// <summary>Whether every sense passed.</summary>
    public bool Passed => SmellOk && ColourOk && TasteOk;

    /// <summary>The senses that failed, named as the panel names its other measures.</summary>
    public IReadOnlyList<string> Failures
    {
        get
        {
            var failed = new List<string>(3);

            if (!SmellOk)
            {
                failed.Add("Smell");
            }

            if (!ColourOk)
            {
                failed.Add("Colour");
            }

            if (!TasteOk)
            {
                failed.Add("Taste");
            }

            return failed;
        }
    }
}
