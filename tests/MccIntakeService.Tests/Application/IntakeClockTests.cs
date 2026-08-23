using MccIntakeService.Application.Abstractions;
using MccIntakeService.Configuration;
using Microsoft.Extensions.Options;

namespace MccIntakeService.Tests.Application;

public class IntakeClockTests
{
    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTimeOffset utcNow) => UtcNow = utcNow;

        public DateTimeOffset UtcNow { get; }
    }

    private static IntakeClock CreateClock(DateTimeOffset utcNow, string cutoff = "16:00")
    {
        var options = new IntakeOptions { DailyCutoff = cutoff, TimeZone = "Asia/Colombo" };

        return new IntakeClock(new FixedClock(utcNow), new StaticOptionsMonitor<IntakeOptions>(options));
    }

    [Fact]
    public void The_local_time_is_the_wall_clock_at_the_centre_not_the_servers_own_time()
    {
        // 02:30 UTC is 08:00 in Colombo (UTC+05:30).
        var clock = CreateClock(new DateTimeOffset(2026, 8, 23, 2, 30, 0, TimeSpan.Zero));

        Assert.Equal(new DateTime(2026, 8, 23, 8, 0, 0), clock.LocalNow);
    }

    [Fact]
    public void An_instant_late_in_the_UTC_day_converts_to_the_following_local_day()
    {
        // 20:00 UTC on the 23rd is 01:30 on the 24th in Colombo.
        var clock = CreateClock(new DateTimeOffset(2026, 8, 23, 20, 0, 0, TimeSpan.Zero));

        Assert.Equal(new DateTime(2026, 8, 24, 1, 30, 0), clock.LocalNow);
        Assert.Equal(new DateOnly(2026, 8, 24), DateOnly.FromDateTime(clock.LocalNow));
    }

    [Fact]
    public void The_daily_cutoff_comes_from_configuration()
    {
        var clock = CreateClock(new DateTimeOffset(2026, 8, 23, 2, 30, 0, TimeSpan.Zero), cutoff: "14:45");

        Assert.Equal(new TimeOnly(14, 45), clock.DailyCutoff);
    }

    [Fact]
    public void The_underlying_instant_is_passed_through_unchanged()
    {
        var instant = new DateTimeOffset(2026, 8, 23, 2, 30, 0, TimeSpan.Zero);

        Assert.Equal(instant, CreateClock(instant).UtcNow);
    }

    [Fact]
    public void An_arbitrary_instant_can_be_converted_to_centre_time()
    {
        var clock = CreateClock(DateTimeOffset.UtcNow);

        var local = clock.ToLocal(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal(new DateTime(2026, 1, 1, 5, 30, 0), local);
    }
}

/// <summary>A minimal <see cref="IOptionsMonitor{T}"/> serving one fixed value.</summary>
internal sealed class StaticOptionsMonitor<T> : IOptionsMonitor<T>
{
    public StaticOptionsMonitor(T value) => CurrentValue = value;

    public T CurrentValue { get; }

    public T Get(string? name) => CurrentValue;

    public IDisposable? OnChange(Action<T, string?> listener) => null;
}
