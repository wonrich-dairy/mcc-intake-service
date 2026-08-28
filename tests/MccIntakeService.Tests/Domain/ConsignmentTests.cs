using MccIntakeService.Domain.Common;
using MccIntakeService.Domain.Consignments;
using MccIntakeService.Domain.Societies;
using MccIntakeService.Tests.Support;

namespace MccIntakeService.Tests.Domain;

public class ConsignmentTests
{
    private static readonly Society Kandy =
        new(Guid.NewGuid(), "KC", "Kandy Co-operative Dairy Society", "KC");

    private static readonly TimeOnly Cutoff = new(16, 0);

    private static readonly DateTime MorningArrival = new(2026, 8, 23, 7, 40, 0);

    private static readonly DateTime NowLocal = new(2026, 8, 23, 8, 0, 0);

    private static Consignment RegisterWith(
        IReadOnlyCollection<CanEntry>? cans = null,
        DateTime? arrival = null,
        DateTime? now = null,
        Society? society = null)
    {
        var arrivalAtLocal = arrival ?? MorningArrival;

        return Consignment.Register(
            Guid.NewGuid(),
            "MCC-20260823-KC-01",
            society ?? Kandy,
            arrivalAtLocal,
            cans ?? [new CanEntry(1, 40.5m), new CanEntry(2, 39.5m)],
            TestIntake.DensityKgPerLitre,
            Cutoff,
            now ?? NowLocal,
            new DateTimeOffset(arrivalAtLocal, TimeSpan.FromMinutes(330)));
    }

    [Fact]
    public void Register_totals_the_quantity_from_the_can_entries()
    {
        var consignment = RegisterWith([new CanEntry(1, 40.5m), new CanEntry(2, 39.5m), new CanEntry(3, 20m)]);

        Assert.Equal(100m, consignment.TotalQuantityKg);
        Assert.Equal(3, consignment.Cans.Count);
    }

    [Fact]
    public void Register_labels_each_can_with_the_society_prefix_and_a_two_digit_number()
    {
        var consignment = RegisterWith([new CanEntry(1, 40m), new CanEntry(12, 30m)]);

        Assert.Equal(["KC 01", "KC 12"], consignment.Cans.Select(can => can.CanLabel));
    }

    [Fact]
    public void Register_records_the_arrival_date_and_an_initial_status_of_registered()
    {
        var consignment = RegisterWith();

        Assert.Equal(new DateOnly(2026, 8, 23), consignment.ArrivalDate);
        Assert.Equal(ConsignmentStatus.Registered, consignment.Status);
        Assert.Equal(MorningArrival, consignment.ArrivalAtLocal);
    }

    [Fact]
    public void Register_rejects_a_consignment_with_no_cans()
    {
        var exception = Assert.Throws<DomainValidationException>(() => RegisterWith([]));

        Assert.Contains("at least one can", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Register_rejects_the_same_can_entered_twice()
    {
        var exception = Assert.Throws<DomainValidationException>(
            () => RegisterWith([new CanEntry(1, 40m), new CanEntry(1, 35m)]));

        Assert.Contains("KC 01", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Register_rejects_a_society_that_is_no_longer_active()
    {
        var retired = new Society(Guid.NewGuid(), "OLD", "Retired Society", "OLD", isActive: false);

        var exception = Assert.Throws<DomainValidationException>(() => RegisterWith(society: retired));

        Assert.Contains("no longer active", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Register_rejects_a_blank_reference()
    {
        var exception = Assert.Throws<DomainValidationException>(() => Consignment.Register(
            Guid.NewGuid(),
            "   ",
            Kandy,
            MorningArrival,
            [new CanEntry(1, 40m)],
            TestIntake.DensityKgPerLitre,
            Cutoff,
            NowLocal,
            DateTimeOffset.UtcNow));

        Assert.Contains("reference is required", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Register_blocks_a_consignment_arriving_after_the_daily_cutoff()
    {
        var lateArrival = new DateTime(2026, 8, 23, 16, 1, 0);
        var laterNow = new DateTime(2026, 8, 23, 16, 5, 0);

        var exception = Assert.Throws<IntakeCutoffExceededException>(
            () => RegisterWith(arrival: lateArrival, now: laterNow));

        Assert.Equal(Cutoff, exception.Cutoff);
        Assert.Equal(new TimeOnly(16, 1), exception.ArrivalTimeOfDay);
    }

    [Fact]
    public void The_cutoff_message_states_the_cutoff_and_the_arrival_time()
    {
        var exception = new IntakeCutoffExceededException(new TimeOnly(16, 0), new TimeOnly(17, 30));

        Assert.Contains("16:00", exception.Message, StringComparison.Ordinal);
        Assert.Contains("17:30", exception.Message, StringComparison.Ordinal);
        Assert.Equal("intake_cutoff_exceeded", exception.Code);
    }

    [Fact]
    public void Register_accepts_a_consignment_arriving_exactly_on_the_cutoff()
    {
        var onTheCutoff = new DateTime(2026, 8, 23, 16, 0, 0);

        var consignment = RegisterWith(arrival: onTheCutoff, now: onTheCutoff);

        Assert.Equal(onTheCutoff, consignment.ArrivalAtLocal);
    }

    [Fact]
    public void Register_rejects_an_arrival_time_in_the_future()
    {
        var arrival = new DateTime(2026, 8, 23, 10, 0, 0);
        var now = new DateTime(2026, 8, 23, 9, 0, 0);

        var exception = Assert.Throws<DomainValidationException>(() => RegisterWith(arrival: arrival, now: now));

        Assert.Contains("future", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Register_tolerates_a_small_clock_skew_between_device_and_server()
    {
        var arrival = new DateTime(2026, 8, 23, 9, 0, 30);
        var now = new DateTime(2026, 8, 23, 9, 0, 0);

        var consignment = RegisterWith(arrival: arrival, now: now);

        Assert.Equal(arrival, consignment.ArrivalAtLocal);
    }

    [Fact]
    public void Register_rejects_a_null_society()
    {
        Assert.Throws<ArgumentNullException>(() => Consignment.Register(
            Guid.NewGuid(),
            "MCC-20260823-KC-01",
            null!,
            MorningArrival,
            [new CanEntry(1, 40m)],
            TestIntake.DensityKgPerLitre,
            Cutoff,
            NowLocal,
            DateTimeOffset.UtcNow));
    }
}
