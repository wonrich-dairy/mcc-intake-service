namespace Wonrich.QualityPanel;

/// <summary>
/// The KQ colour scale, as a fixed enumeration (SCRUM-50). The dye retains its colour in fresh
/// milk and is reduced through to white as microbial activity increases, so the further down this
/// scale a sample reads, the worse it is.
/// </summary>
/// <remarks>
/// The shades are the seven the officer's card carries, as listed in SCRUM-7. The numeric values
/// are part of the contract: they are stored against panels and compared across checkpoints, so
/// they must not be renumbered. New shades go on the end.
/// </remarks>
public enum KqColour
{
    /// <summary>Dye unreduced. Fresh milk.</summary>
    Blue = 0,

    /// <summary>Very slight reduction.</summary>
    LightBlue = 1,

    /// <summary>Slight reduction.</summary>
    Purple = 2,

    /// <summary>Moderate reduction.</summary>
    PurplePink = 3,

    /// <summary>Marked reduction.</summary>
    LightPink = 4,

    /// <summary>Heavy reduction.</summary>
    Pink = 5,

    /// <summary>Dye fully reduced. Heavy microbial load.</summary>
    White = 6
}

/// <summary>Helpers over the KQ scale.</summary>
public static class KqColourScale
{
    /// <summary>Every shade, best first.</summary>
    public static readonly IReadOnlyList<KqColour> All =
    [
        KqColour.Blue,
        KqColour.LightBlue,
        KqColour.Purple,
        KqColour.PurplePink,
        KqColour.LightPink,
        KqColour.Pink,
        KqColour.White
    ];

    /// <summary>Whether the value is one of the defined shades.</summary>
    public static bool IsDefined(KqColour colour) => All.Contains(colour);

    /// <summary>
    /// Whether <paramref name="colour"/> is no worse than <paramref name="worstAcceptable"/>.
    /// Ordering is by the enum value, which runs best to worst.
    /// </summary>
    public static bool IsAtLeastAsGoodAs(KqColour colour, KqColour worstAcceptable) =>
        colour <= worstAcceptable;
}
