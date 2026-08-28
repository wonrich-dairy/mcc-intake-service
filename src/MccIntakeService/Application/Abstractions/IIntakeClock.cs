namespace MccIntakeService.Application.Abstractions;

/// <summary>
/// The clock as the chilling centre experiences it: wall-clock time in the centre's own zone,
/// together with the daily cutoff that governs whether milk can still be taken in.
/// </summary>
public interface IIntakeClock
{
    DateTimeOffset UtcNow { get; }

    /// <summary>Current wall-clock time at the centre.</summary>
    DateTime LocalNow { get; }

    /// <summary>Configured local time after which intake closes.</summary>
    TimeOnly DailyCutoff { get; }

    /// <summary>Converts an instant to the centre's wall-clock time.</summary>
    DateTime ToLocal(DateTimeOffset instant);
}
