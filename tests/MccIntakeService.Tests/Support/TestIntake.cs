namespace MccIntakeService.Tests.Support;

/// <summary>Centre operating parameters the tests register consignments under.</summary>
internal static class TestIntake
{
    /// <summary>
    /// Milk density used to derive litres from the weight recorded at the gate. Matches the
    /// default in <see cref="MccIntakeService.Configuration.IntakeOptions"/>.
    /// </summary>
    public const decimal DensityKgPerLitre = 1.03m;
}
