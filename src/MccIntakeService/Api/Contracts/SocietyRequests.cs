using System.ComponentModel.DataAnnotations;
using MccIntakeService.Domain.Societies;

namespace MccIntakeService.Api.Contracts;

/// <summary>Details an MCC Manager supplies when registering a supplying society (SCRUM-51).</summary>
public sealed class SaveSocietyRequest
{
    /// <summary>
    /// Short society code, unique across the centre, used as the SOCIETY segment of every
    /// consignment reference. Cannot be changed once consignments exist against the society.
    /// </summary>
    /// <example>TH</example>
    [Required(ErrorMessage = "A society code is required.")]
    [StringLength(Society.MaxCodeLength, MinimumLength = 1)]
    public string Code { get; set; } = string.Empty;

    /// <summary>Full society name.</summary>
    /// <example>Thalawakele Tea Country Milk Society</example>
    [Required(ErrorMessage = "A society name is required.")]
    [StringLength(Society.MaxNameLength, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Prefix stamped on the society's physical cans, e.g. "TH" in the label "TH 01".</summary>
    /// <example>TH</example>
    [Required(ErrorMessage = "A can label prefix is required.")]
    [StringLength(Society.MaxCodeLength, MinimumLength = 1)]
    public string CanLabelPrefix { get; set; } = string.Empty;

    /// <summary>Person the centre deals with at this society.</summary>
    /// <example>Sunil Perera</example>
    [StringLength(Society.MaxContactPersonLength)]
    public string? ContactPerson { get; set; }

    /// <summary>Telephone number for the society contact.</summary>
    /// <example>+94 81 222 3344</example>
    [StringLength(Society.MaxContactNumberLength)]
    public string? ContactNumber { get; set; }
}
