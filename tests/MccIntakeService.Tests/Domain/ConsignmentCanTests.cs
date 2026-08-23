using MccIntakeService.Domain.Common;
using MccIntakeService.Domain.Consignments;
using MccIntakeService.Domain.Societies;

namespace MccIntakeService.Tests.Domain;

public class ConsignmentCanTests
{
    private static readonly Society Kandy =
        new(Guid.NewGuid(), "KC", "Kandy Co-operative Dairy Society", "KC");

    private static Consignment RegisterWithSingleCan(int canNumber, decimal quantityLitres) =>
        Consignment.Register(
            Guid.NewGuid(),
            "MCC-20260823-KC-01",
            Kandy,
            new DateTime(2026, 8, 23, 7, 0, 0),
            [new CanEntry(canNumber, quantityLitres)],
            new TimeOnly(16, 0),
            new DateTime(2026, 8, 23, 8, 0, 0),
            DateTimeOffset.UtcNow);

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void A_can_number_must_be_positive(int canNumber)
    {
        Assert.Throws<DomainValidationException>(() => RegisterWithSingleCan(canNumber, 40m));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-12.5)]
    public void A_can_must_carry_a_quantity_greater_than_zero(decimal quantityLitres)
    {
        var exception = Assert.Throws<DomainValidationException>(() => RegisterWithSingleCan(1, quantityLitres));

        Assert.Contains("greater than zero", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_can_quantity_beyond_the_physical_limit_is_rejected()
    {
        var exception = Assert.Throws<DomainValidationException>(
            () => RegisterWithSingleCan(1, ConsignmentCan.MaxQuantityLitres + 0.01m));

        Assert.Contains("exceeds", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_can_quantity_is_rounded_to_two_decimal_places()
    {
        var consignment = RegisterWithSingleCan(1, 40.456m);

        Assert.Equal(40.46m, consignment.Cans.Single().QuantityLitres);
    }

    [Fact]
    public void Cans_are_stored_in_can_number_order_regardless_of_entry_order()
    {
        var consignment = Consignment.Register(
            Guid.NewGuid(),
            "MCC-20260823-KC-01",
            Kandy,
            new DateTime(2026, 8, 23, 7, 0, 0),
            [new CanEntry(5, 10m), new CanEntry(2, 20m), new CanEntry(9, 30m)],
            new TimeOnly(16, 0),
            new DateTime(2026, 8, 23, 8, 0, 0),
            DateTimeOffset.UtcNow);

        Assert.Equal([2, 5, 9], consignment.Cans.Select(can => can.CanNumber));
    }
}
