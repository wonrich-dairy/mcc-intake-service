using MccIntakeService.Domain.Common;

namespace MccIntakeService.Domain.Societies;

/// <summary>
/// A supplying milk society registered with the chilling centre. Consignments may only be
/// registered against a society that exists here — free-text societies are rejected (SCRUM-6).
/// Full lifecycle management of societies and their can labels is owned by SCRUM-51.
/// </summary>
public class Society
{
    /// <summary>EF Core materialisation constructor.</summary>
    private Society()
    {
        Code = string.Empty;
        Name = string.Empty;
        CanLabelPrefix = string.Empty;
    }

    public Society(Guid id, string code, string name, string canLabelPrefix, bool isActive = true)
    {
        Id = id;
        Code = NormaliseCode(code);
        Name = string.IsNullOrWhiteSpace(name)
            ? throw new DomainValidationException("Society name is required.")
            : name.Trim();
        CanLabelPrefix = NormaliseCode(canLabelPrefix);
        IsActive = isActive;
    }

    public Guid Id { get; private set; }

    /// <summary>Short uppercase society code, used as the SOCIETY segment of a consignment reference.</summary>
    public string Code { get; private set; }

    /// <summary>Human-readable society name.</summary>
    public string Name { get; private set; }

    /// <summary>Prefix stamped on the society's physical cans, e.g. "KC" in the label "KC 01".</summary>
    public string CanLabelPrefix { get; private set; }

    /// <summary>Inactive societies remain for historical traceability but cannot receive new consignments.</summary>
    public bool IsActive { get; private set; }

    private static string NormaliseCode(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainValidationException("Society code and can label prefix are required.");
        }

        return value.Trim().ToUpperInvariant();
    }
}
