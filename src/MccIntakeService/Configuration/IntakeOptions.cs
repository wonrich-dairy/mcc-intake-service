using System.ComponentModel.DataAnnotations;

namespace MccIntakeService.Configuration;

/// <summary>
/// Operating parameters of the chilling centre, bound from the "Intake" configuration section.
/// </summary>
public sealed class IntakeOptions
{
    public const string SectionName = "Intake";

    /// <summary>
    /// Local time of day after which milk is no longer accepted, as HH:mm (SCRUM-6).
    /// </summary>
    [Required]
    [RegularExpression(@"^([01]\d|2[0-3]):[0-5]\d$", ErrorMessage = "Intake:DailyCutoff must be a 24-hour time of the form HH:mm.")]
    public string DailyCutoff { get; set; } = "16:00";

    /// <summary>
    /// Time zone the centre operates in. Arrival times and intake dates are wall-clock values in this zone,
    /// so the daily cutoff and the reference date do not drift with the server's own locale.
    /// </summary>
    [Required]
    public string TimeZone { get; set; } = "Asia/Colombo";

    /// <summary>The cutoff parsed into a <see cref="TimeOnly"/>.</summary>
    public TimeOnly ParsedDailyCutoff =>
        TimeOnly.TryParseExact(DailyCutoff, "HH:mm", out var cutoff)
            ? cutoff
            : throw new InvalidOperationException($"Intake:DailyCutoff '{DailyCutoff}' is not a valid HH:mm time.");

    /// <summary>The configured zone resolved into a <see cref="TimeZoneInfo"/>.</summary>
    public TimeZoneInfo ResolvedTimeZone
    {
        get
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(TimeZone);
            }
            catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
                throw new InvalidOperationException(
                    $"Intake:TimeZone '{TimeZone}' is not a time zone this host recognises.", exception);
            }
        }
    }
}
