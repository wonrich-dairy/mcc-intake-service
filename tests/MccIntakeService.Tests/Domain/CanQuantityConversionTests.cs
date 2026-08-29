using MccIntakeService.Domain.Consignments;
using MccIntakeService.Domain.Societies;
using MccIntakeService.Tests.Support;

namespace MccIntakeService.Tests.Domain;

/// <summary>
/// Covers the weight the gate records and the litres derived from it. Kilograms are the
/// measurement; litres are computed at registration using the centre's configured density.
/// </summary>
public class CanQuantityConversionTests
{
    private static readonly Society Kandy =
        new(Guid.NewGuid(), "KC", "Kandy Co-operative Dairy Society", "KC");

    private static Consignment RegisterWith(
        IReadOnlyCollection<CanEntry> cans,
        decimal density = TestIntake.DensityKgPerLitre) =>
        Consignment.Register(
            Guid.NewGuid(),
            "MCC-20260823-KC-01",
            Kandy,
            new DateTime(2026, 8, 23, 7, 0, 0),
            cans,
            density,
            new TimeOnly(16, 0),
            new DateTime(2026, 8, 23, 8, 0, 0),
            DateTimeOffset.UtcNow);

    [Fact]
    public void The_weight_entered_is_kept_and_litres_are_derived_from_it()
    {
        var can = RegisterWith([new CanEntry(1, 41.20m)]).Cans.Single();

        Assert.Equal(41.20m, can.QuantityKg);
        Assert.Equal(40.00m, can.QuantityLitres);
    }

    [Fact]
    public void Litres_are_rounded_to_two_decimal_places()
    {
        // 40.46 kg / 1.03 = 39.2815..., which has to land on a storable two-decimal figure.
        var can = RegisterWith([new CanEntry(1, 40.456m)]).Cans.Single();

        Assert.Equal(40.46m, can.QuantityKg);
        Assert.Equal(39.28m, can.QuantityLitres);
    }

    [Fact]
    public void A_denser_configuration_yields_fewer_litres_for_the_same_weight()
    {
        var lighter = RegisterWith([new CanEntry(1, 41.20m)], density: 1.00m).Cans.Single();
        var denser = RegisterWith([new CanEntry(1, 41.20m)], density: 1.03m).Cans.Single();

        Assert.Equal(41.20m, lighter.QuantityLitres);
        Assert.Equal(40.00m, denser.QuantityLitres);
        Assert.Equal(lighter.QuantityKg, denser.QuantityKg);
    }

    [Fact]
    public void Both_totals_are_summed_from_the_can_breakdown()
    {
        var consignment = RegisterWith([new CanEntry(1, 41.20m), new CanEntry(2, 20.60m)]);

        Assert.Equal(61.80m, consignment.TotalQuantityKg);
        Assert.Equal(60.00m, consignment.TotalQuantityLitres);

        // The totals must agree with the breakdown the officer can see, so they are summed from
        // the cans rather than converted from the total weight.
        Assert.Equal(consignment.Cans.Sum(can => can.QuantityLitres), consignment.TotalQuantityLitres);
        Assert.Equal(consignment.Cans.Sum(can => can.QuantityKg), consignment.TotalQuantityKg);
    }

    [Fact]
    public void A_density_of_zero_or_less_is_rejected()
    {
        Assert.Throws<MccIntakeService.Domain.Common.DomainValidationException>(
            () => RegisterWith([new CanEntry(1, 41.20m)], density: 0m));
    }
}
