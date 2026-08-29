using MccIntakeService.Domain.Common;

namespace MccIntakeService.Domain.Societies;

/// <summary>
/// A supplying milk society registered with the chilling centre. Consignments may only be
/// registered against a society that exists here — free-text societies are rejected (SCRUM-6).
/// Societies are created, amended and deactivated by an MCC Manager (SCRUM-51); they are never
/// deleted, because historical consignments must keep resolving to their source.
/// </summary>
public class Society
{
    public const int MaxCodeLength = 10;
    public const int MaxNameLength = 200;
    public const int MaxContactPersonLength = 150;
    public const int MaxContactNumberLength = 30;

    /// <summary>EF Core materialisation constructor.</summary>
    private Society()
    {
        Code = string.Empty;
        Name = string.Empty;
        CanLabelPrefix = string.Empty;
    }

    public Society(
        Guid id,
        string code,
        string name,
        string canLabelPrefix,
        string? contactPerson = null,
        string? contactNumber = null,
        bool isActive = true)
    {
        Id = id;
        Code = NormaliseCode(code);
        Name = NormaliseName(name);
        CanLabelPrefix = NormaliseCode(canLabelPrefix);
        ContactPerson = NormaliseOptional(contactPerson, MaxContactPersonLength, nameof(ContactPerson));
        ContactNumber = NormaliseOptional(contactNumber, MaxContactNumberLength, nameof(ContactNumber));
        IsActive = isActive;
    }

    public Guid Id { get; private set; }

    /// <summary>Short uppercase society code, used as the SOCIETY segment of a consignment reference.</summary>
    public string Code { get; private set; }

    /// <summary>Human-readable society name.</summary>
    public string Name { get; private set; }

    /// <summary>Prefix stamped on the society's physical cans, e.g. "KC" in the label "KC 01".</summary>
    public string CanLabelPrefix { get; private set; }

    /// <summary>Name of the person the centre deals with at this society.</summary>
    public string? ContactPerson { get; private set; }

    /// <summary>Telephone number for the society contact.</summary>
    public string? ContactNumber { get; private set; }

    /// <summary>Inactive societies remain for historical traceability but cannot receive new consignments.</summary>
    public bool IsActive { get; private set; }

    /// <summary>Amends the details that are always safe to change.</summary>
    public void UpdateDetails(string name, string canLabelPrefix, string? contactPerson, string? contactNumber)
    {
        Name = NormaliseName(name);
        CanLabelPrefix = NormaliseCode(canLabelPrefix);
        ContactPerson = NormaliseOptional(contactPerson, MaxContactPersonLength, nameof(ContactPerson));
        ContactNumber = NormaliseOptional(contactNumber, MaxContactNumberLength, nameof(ContactNumber));
    }

    /// <summary>
    /// Changes the society code. Once consignments exist against the society the code is frozen:
    /// it is baked into every reference already issued, so changing it would strand that history.
    /// </summary>
    /// <param name="code">The new code.</param>
    /// <param name="hasConsignments">Whether any consignment already refers to this society.</param>
    public void ChangeCode(string code, bool hasConsignments)
    {
        var normalised = NormaliseCode(code);

        if (normalised == Code)
        {
            return;
        }

        if (hasConsignments)
        {
            throw new DomainValidationException(
                $"Society code '{Code}' cannot be changed because consignments have already been registered against it.");
        }

        Code = normalised;
    }

    /// <summary>Retires the society. Existing records keep resolving to it; new ones cannot select it.</summary>
    public void Deactivate() => IsActive = false;

    /// <summary>Returns a retired society to service.</summary>
    public void Reactivate() => IsActive = true;

    private static string NormaliseName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainValidationException("Society name is required.");
        }

        var trimmed = value.Trim();

        return trimmed.Length > MaxNameLength
            ? throw new DomainValidationException($"Society name cannot exceed {MaxNameLength} characters.")
            : trimmed;
    }

    private static string NormaliseCode(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainValidationException("Society code and can label prefix are required.");
        }

        var trimmed = value.Trim();

        return trimmed.Length > MaxCodeLength
            ? throw new DomainValidationException($"Society code cannot exceed {MaxCodeLength} characters.")
            : trimmed.ToUpperInvariant();
    }

    private static string? NormaliseOptional(string? value, int maxLength, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();

        return trimmed.Length > maxLength
            ? throw new DomainValidationException($"{field} cannot exceed {maxLength} characters.")
            : trimmed;
    }
}
