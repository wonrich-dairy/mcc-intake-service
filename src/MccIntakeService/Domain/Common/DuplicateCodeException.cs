namespace MccIntakeService.Domain.Common;

/// <summary>
/// Raised when a society code that is already in use is submitted (SCRUM-51). Distinct from a
/// plain validation failure so the API can answer 409 Conflict rather than 400.
/// </summary>
public sealed class DuplicateCodeException : DomainException
{
    public DuplicateCodeException(string entity, string conflictingCode)
        : base("duplicate_code", $"{entity} code '{conflictingCode}' is already in use.")
    {
        Entity = entity;
        ConflictingCode = conflictingCode;
    }

    /// <summary>The kind of record the collision happened on, e.g. "Society".</summary>
    public string Entity { get; }

    /// <summary>The code that was already taken.</summary>
    public string ConflictingCode { get; }
}
