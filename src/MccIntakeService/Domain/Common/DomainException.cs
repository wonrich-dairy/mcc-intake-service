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

/// <summary>
/// Raised when a consignment already sitting in a tank is poured again (SCRUM-52). Separate from
/// <see cref="DomainValidationException"/> so the API answers 409 from <see cref="DomainException.Code"/>
/// rather than by matching on the message, which reworded would silently degrade to a 400.
/// </summary>
public sealed class ConsignmentAlreadyPouredException : DomainException
{
    public ConsignmentAlreadyPouredException(string reference)
        : base(
            "consignment_already_poured",
            $"Consignment {reference} has already been poured and cannot be poured again.")
    {
        Reference = reference;
    }

    /// <summary>Gate reference of the consignment already in a tank.</summary>
    public string Reference { get; }
}

/// <summary>
/// Raised when a consignment that already carries a gate verdict is tested again (SCRUM-7).
/// Separate from <see cref="DomainValidationException"/> for the same reason
/// <see cref="ConsignmentAlreadyPouredException"/> is: the API answers 409 from
/// <see cref="DomainException.Code"/> rather than by matching on the message.
/// </summary>
public sealed class ConsignmentAlreadyTestedException : DomainException
{
    public ConsignmentAlreadyTestedException(string reference, string? status = null)
        : base(
            "consignment_already_tested",
            status is null
                ? $"Consignment {reference} has already been tested."
                : $"Consignment {reference} has already been tested and is {status}.")
    {
        Reference = reference;
        Status = status;
    }

    /// <summary>Gate reference of the consignment already tested.</summary>
    public string Reference { get; }

    /// <summary>The verdict already settled on it, where the caller knew it.</summary>
    public string? Status { get; }
}

/// <summary>
/// Raised when a dispatch note that has already been screened at factory intake is screened again
/// (SCRUM-9). Distinct type for the same reason as the two above: screening a note twice would
/// leave two answers about one bowser, and that refusal is a 409 a consumer branches on.
/// </summary>
public sealed class ArrivalAlreadyScreenedException : DomainException
{
    public ArrivalAlreadyScreenedException(string dispatchNoteReference)
        : base(
            "arrival_already_screened",
            $"Dispatch note {dispatchNoteReference} has already been screened.")
    {
        DispatchNoteReference = dispatchNoteReference;
    }

    /// <summary>Reference of the note already screened.</summary>
    public string DispatchNoteReference { get; }
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
