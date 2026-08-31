namespace Wonrich.QualityPanel;

/// <summary>
/// The composition calculations shared by every checkpoint (SCRUM-50). Held here once so the MCC
/// gate and the lab cannot drift apart on what the same readings mean.
/// </summary>
public static class MilkComposition
{
    /// <summary>
    /// Temperature the lactometer is calibrated at. A reading taken at any other temperature has
    /// to be corrected before it means anything, because milk density varies with temperature.
    /// </summary>
    public const decimal CalibrationTemperatureCelsius = 27m;

    /// <summary>
    /// CLR degrees to add per °C above the calibration temperature, and subtract per °C below.
    /// </summary>
    public const decimal CorrectionPerDegree = 0.2m;

    /// <summary>Results are carried to two decimals; readings are never more precise than that.</summary>
    private const int Precision = 2;

    /// <summary>
    /// Corrects a raw lactometer reading for the temperature the sample was at.
    /// </summary>
    /// <remarks>
    /// Warmer milk is less dense, so it reads low and the correction adds; colder milk reads high
    /// and the correction subtracts. At exactly the calibration temperature the reading stands.
    /// </remarks>
    /// <param name="rawLactometerReading">The lactometer reading as taken.</param>
    /// <param name="temperatureCelsius">Temperature of the sample when read.</param>
    public static decimal CorrectedClr(decimal rawLactometerReading, decimal temperatureCelsius) =>
        Round(rawLactometerReading
            + (CorrectionPerDegree * (temperatureCelsius - CalibrationTemperatureCelsius)));

    /// <summary>
    /// Solids-not-fat: (FAT × 0.22) + (CLR × 0.25) + 0.72.
    /// </summary>
    /// <param name="fatPercent">Fat percentage from the butyrometer.</param>
    /// <param name="correctedClr">CLR already corrected by <see cref="CorrectedClr"/>.</param>
    public static decimal Snf(decimal fatPercent, decimal correctedClr) =>
        Round((fatPercent * 0.22m) + (correctedClr * 0.25m) + 0.72m);

    /// <summary>Total solids: SNF + FAT.</summary>
    /// <param name="snf">Solids-not-fat, from <see cref="Snf"/>.</param>
    /// <param name="fatPercent">Fat percentage.</param>
    public static decimal TotalSolids(decimal snf, decimal fatPercent) => Round(snf + fatPercent);

    /// <summary>
    /// Runs the whole chain from raw readings: corrects the CLR, then derives SNF and TS from it.
    /// </summary>
    /// <remarks>
    /// SNF is computed from the <em>corrected</em> CLR. Feeding it the raw reading is the easiest
    /// mistake to make here, and it silently shifts every downstream figure.
    /// </remarks>
    public static CompositionResult From(
        decimal fatPercent,
        decimal rawLactometerReading,
        decimal temperatureCelsius)
    {
        var clr = CorrectedClr(rawLactometerReading, temperatureCelsius);
        var snf = Snf(fatPercent, clr);

        return new CompositionResult(fatPercent, clr, snf, TotalSolids(snf, fatPercent));
    }

    private static decimal Round(decimal value) =>
        decimal.Round(value, Precision, MidpointRounding.AwayFromZero);
}

/// <summary>The derived composition of one sample.</summary>
/// <param name="FatPercent">Fat percentage, as measured.</param>
/// <param name="CorrectedClr">Lactometer reading corrected to the calibration temperature.</param>
/// <param name="Snf">Solids-not-fat.</param>
/// <param name="TotalSolids">Total solids.</param>
public sealed record CompositionResult(
    decimal FatPercent,
    decimal CorrectedClr,
    decimal Snf,
    decimal TotalSolids);
