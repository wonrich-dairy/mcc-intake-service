namespace MccIntakeService.Application.Abstractions;

/// <summary>Supplies the current instant. Abstracted so time-sensitive rules can be tested deterministically.</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

/// <summary>Default <see cref="IClock"/> backed by the system clock.</summary>
public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
