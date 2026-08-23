using MccIntakeService.Application.Abstractions;

namespace MccIntakeService.Tests.Application;

public class SystemClockTests
{
    [Fact]
    public void The_system_clock_reports_the_current_instant()
    {
        var before = DateTimeOffset.UtcNow;
        var reported = new SystemClock().UtcNow;
        var after = DateTimeOffset.UtcNow;

        Assert.InRange(reported, before, after);
    }
}
