using System.Globalization;

namespace MccIntakeService.Domain.Common;

/// <summary>
/// Base type for rule violations raised by the domain model. Each exception carries a stable
/// <see cref="Code"/> so the API layer can map it onto a ProblemDetails response without
/// string-matching on messages.
/// </summary>
public abstract class DomainException : Exception
{
    protected DomainException(string code, string message) : base(message)
    {
        Code = code;
    }

    /// <summary>Stable, machine-readable identifier for the violated rule.</summary>
    public string Code { get; }
}

/// <summary>Raised when a command carries values the domain cannot accept.</summary>
public sealed class DomainValidationException : DomainException
{
    public DomainValidationException(string message) : base("domain_validation_failed", message)
    {
    }
}

/// <summary>Raised when a consignment arrives after the configured daily intake cutoff (SCRUM-6).</summary>
public sealed class IntakeCutoffExceededException : DomainException
{
    private const string TimeFormat = "HH:mm";

    public IntakeCutoffExceededException(TimeOnly cutoff, TimeOnly arrivalTimeOfDay)
        : base("intake_cutoff_exceeded", BuildMessage(cutoff, arrivalTimeOfDay))
    {
        Cutoff = cutoff;
        ArrivalTimeOfDay = arrivalTimeOfDay;
    }

    private static string BuildMessage(TimeOnly cutoff, TimeOnly arrivalTimeOfDay)
    {
        var closes = cutoff.ToString(TimeFormat, CultureInfo.InvariantCulture);
        var arrived = arrivalTimeOfDay.ToString(TimeFormat, CultureInfo.InvariantCulture);

        return $"Milk intake closes at {closes}. This consignment arrived at {arrived} and cannot be registered for that day.";
    }

    /// <summary>The configured daily cutoff that was breached.</summary>
    public TimeOnly Cutoff { get; }

    /// <summary>The local time of day the consignment arrived.</summary>
    public TimeOnly ArrivalTimeOfDay { get; }
}

/// <summary>Raised when a referenced entity does not exist.</summary>
public sealed class EntityNotFoundException : DomainException
{
    public EntityNotFoundException(string entity, string identifier)
        : base("entity_not_found", $"{entity} '{identifier}' was not found.")
    {
        Entity = entity;
        Identifier = identifier;
    }

    public string Entity { get; }

    public string Identifier { get; }
}
