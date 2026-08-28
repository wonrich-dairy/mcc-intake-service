using MccIntakeService.Configuration;
using Microsoft.Extensions.Options;

namespace MccIntakeService.Application.Abstractions;

/// <inheritdoc cref="IIntakeClock" />
public sealed class IntakeClock : IIntakeClock
{
    private readonly IClock _clock;
    private readonly IOptionsMonitor<IntakeOptions> _options;

    public IntakeClock(IClock clock, IOptionsMonitor<IntakeOptions> options)
    {
        _clock = clock;
        _options = options;
    }

    public DateTimeOffset UtcNow => _clock.UtcNow;

    public DateTime LocalNow => ToLocal(_clock.UtcNow);

    public TimeOnly DailyCutoff => _options.CurrentValue.ParsedDailyCutoff;

    public DateTime ToLocal(DateTimeOffset instant) =>
        TimeZoneInfo.ConvertTime(instant, _options.CurrentValue.ResolvedTimeZone).DateTime;
}
