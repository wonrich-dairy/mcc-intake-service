namespace Wonrich.QualityPanel;

/// <summary>
/// The KQ colour scale, as a fixed enumeration (SCRUM-50). The dye retains its colour in fresh
/// milk and is reduced towards white as microbial activity increases, so the further down this
/// scale a sample reads, the worse it is.
/// </summary>
/// <remarks>
/// The numeric values are part of the contract: they are stored against panels and compared
/// across checkpoints, so they must not be renumbered. New shades go on the end.
/// </remarks>
public enum KqColour
{
    /// <summary>Dye unreduced. Fresh milk.</summary>
    Blue = 0,

    /// <summary>Slight reduction.</summary>
    BluishGreen = 1,

    /// <summary>Moderate reduction.</summary>
    Green = 2,

    /// <summary>Marked reduction.</summary>
    GreenishYellow = 3,

    /// <summary>Heavy reduction.</summary>
    Yellow = 4,

    /// <summary>Dye fully reduced. Heavy microbial load.</summary>
    White = 5
}

/// <summary>Helpers over the KQ scale.</summary>
public static class KqColourScale
{
    /// <summary>Every shade, best first.</summary>
    public static readonly IReadOnlyList<KqColour> All =
    [
        KqColour.Blue,
        KqColour.BluishGreen,
        KqColour.Green,
        KqColour.GreenishYellow,
        KqColour.Yellow,
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
