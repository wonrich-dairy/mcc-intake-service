using MccIntakeService.Application.Abstractions;

namespace MccIntakeService.Tests.Support;

/// <summary>
/// A clock pinned to a chosen wall-clock time so cutoff behaviour can be tested without
/// depending on when the suite happens to run.
/// </summary>
internal sealed class FakeIntakeClock : IIntakeClock
{
    public FakeIntakeClock(DateTime localNow, TimeOnly? dailyCutoff = null)
    {
        LocalNow = localNow;
        DailyCutoff = dailyCutoff ?? new TimeOnly(16, 0);
    }

    public DateTime LocalNow { get; set; }

    public TimeOnly DailyCutoff { get; set; }

    /// <summary>The centre runs on UTC+05:30 (Asia/Colombo), which the fake mirrors.</summary>
    public TimeSpan Offset { get; init; } = TimeSpan.FromMinutes(330);

    public DateTimeOffset UtcNow => new DateTimeOffset(LocalNow, Offset).ToUniversalTime();

    public DateTime ToLocal(DateTimeOffset instant) => TimeZoneInfo.ConvertTime(instant, TimeZoneInfo.Utc).DateTime + Offset;
}
