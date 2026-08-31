using Wonrich.QualityPanel;

namespace Wonrich.QualityPanel.Tests;

/// <summary>
/// Covers CLR correction either side of the 27 °C calibration point, and SNF and TS against
/// worked examples (SCRUM-50).
/// </summary>
public class MilkCompositionTests
{
    [Fact]
    public void A_reading_at_the_calibration_temperature_stands_unchanged()
    {
        Assert.Equal(28.00m, MilkComposition.CorrectedClr(28.0m, 27.0m));
    }

    [Theory]
    // Warmer milk is less dense, so it reads low and the correction adds.
    [InlineData(28.0, 30.0, 28.60)]
    [InlineData(28.0, 28.0, 28.20)]
    [InlineData(26.5, 32.0, 27.50)]
    [InlineData(30.0, 37.0, 32.00)]
    public void Above_the_calibration_temperature_the_correction_adds(
        decimal raw,
        decimal temperature,
        decimal expected)
    {
        Assert.Equal(expected, MilkComposition.CorrectedClr(raw, temperature));
    }

    [Theory]
    // Colder milk is denser, so it reads high and the correction subtracts.
    [InlineData(28.0, 24.0, 27.40)]
    [InlineData(28.0, 26.0, 27.80)]
    [InlineData(29.5, 22.0, 28.50)]
    [InlineData(30.0, 17.0, 28.00)]
    public void Below_the_calibration_temperature_the_correction_subtracts(
        decimal raw,
        decimal temperature,
        decimal expected)
    {
        Assert.Equal(expected, MilkComposition.CorrectedClr(raw, temperature));
    }

    [Theory]
    // SNF = (FAT × 0.22) + (CLR × 0.25) + 0.72
    [InlineData(4.0, 28.0, 8.60)]   // 0.88 + 7.00 + 0.72
    [InlineData(3.5, 26.0, 7.99)]   // 0.77 + 6.50 + 0.72
    [InlineData(6.0, 30.0, 9.54)]   // 1.32 + 7.50 + 0.72
    [InlineData(0.0, 0.0, 0.72)]    // the constant alone
    public void Snf_matches_the_worked_examples(decimal fat, decimal clr, decimal expected)
    {
        Assert.Equal(expected, MilkComposition.Snf(fat, clr));
    }

    [Theory]
    // TS = SNF + FAT
    [InlineData(8.60, 4.0, 12.60)]
    [InlineData(7.99, 3.5, 11.49)]
    [InlineData(9.54, 6.0, 15.54)]
    public void Total_solids_is_snf_plus_fat(decimal snf, decimal fat, decimal expected)
    {
        Assert.Equal(expected, MilkComposition.TotalSolids(snf, fat));
    }

    [Fact]
    public void The_full_chain_derives_snf_from_the_corrected_clr_not_the_raw_reading()
    {
        // Raw 28.0 at 30 °C corrects to 28.60, so SNF is 0.88 + 7.15 + 0.72 = 8.75.
        // Using the raw reading instead would give 8.60 — the mistake this test exists to catch.
        var result = MilkComposition.From(4.0m, 28.0m, 30.0m);

        Assert.Equal(28.60m, result.CorrectedClr);
        Assert.Equal(8.75m, result.Snf);
        Assert.Equal(12.75m, result.TotalSolids);
        Assert.Equal(4.0m, result.FatPercent);
    }

    [Fact]
    public void The_full_chain_agrees_with_the_individual_calculations()
    {
        var chained = MilkComposition.From(4.2m, 27.5m, 25.0m);

        var clr = MilkComposition.CorrectedClr(27.5m, 25.0m);
        var snf = MilkComposition.Snf(4.2m, clr);

        Assert.Equal(clr, chained.CorrectedClr);
        Assert.Equal(snf, chained.Snf);
        Assert.Equal(MilkComposition.TotalSolids(snf, 4.2m), chained.TotalSolids);
    }

    [Fact]
    public void Results_are_carried_to_two_decimals()
    {
        // 0.7326 + 6.9425 + 0.72 = 8.3951, which must land on a storable two-decimal figure.
        var snf = MilkComposition.Snf(3.33m, 27.77m);

        Assert.True(snf.Scale <= 2);
        Assert.Equal(8.40m, snf);
    }

    [Fact]
    public void The_calibration_constants_are_the_documented_ones()
    {
        Assert.Equal(27m, MilkComposition.CalibrationTemperatureCelsius);
        Assert.Equal(0.2m, MilkComposition.CorrectionPerDegree);
    }
}
