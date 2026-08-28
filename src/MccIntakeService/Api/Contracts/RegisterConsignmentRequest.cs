using System.ComponentModel.DataAnnotations;
using MccIntakeService.Domain.Consignments;

namespace MccIntakeService.Api.Contracts;

/// <summary>
/// The can sheet an intake officer submits when a society delivery arrives at the gate.
/// </summary>
public sealed class RegisterConsignmentRequest
{
    /// <summary>
    /// Identifier of the supplying society, chosen from <c>GET /api/societies</c>.
    /// Societies that are not registered cannot be supplied as free text.
    /// </summary>
    /// <example>6f0f6f1a-0001-4a2b-9c3d-000000000001</example>
    [Required(ErrorMessage = "A supplying society must be selected.")]
    public Guid SocietyId { get; set; }

    /// <summary>
    /// Arrival wall-clock time at the centre. Leave this out and the server captures the current
    /// time; supply it to correct the captured time before submitting. It cannot be changed afterwards.
    /// </summary>
    /// <example>2026-08-23T07:40:00</example>
    public DateTime? ArrivalAtLocal { get; set; }

    /// <summary>The cans delivered in this consignment. At least one is required.</summary>
    [Required(ErrorMessage = "At least one can must be recorded.")]
    [MinLength(1, ErrorMessage = "At least one can must be recorded.")]
    public List<RegisterConsignmentCanRequest> Cans { get; set; } = [];

    /// <summary>Maps the request onto the domain can entries.</summary>
    public IReadOnlyCollection<CanEntry> ToCanEntries() =>
        Cans.Select(can => new CanEntry(can.CanNumber, can.QuantityKg)).ToList();
}

/// <summary>A single can on the consignment sheet.</summary>
public sealed class RegisterConsignmentCanRequest
{
    /// <summary>
    /// The can number within the society numbering. Combined with the society prefix this
    /// produces the printed label, e.g. can number 1 for society KC becomes "KC 01".
    /// </summary>
    /// <example>1</example>
    [Range(1, 999, ErrorMessage = "Can number must be between 1 and 999.")]
    public int CanNumber { get; set; }

    /// <summary>
    /// Kilograms of milk weighed in this can. Litres are derived from this using the centre's
    /// configured milk density and returned on the response; they are not submitted.
    /// </summary>
    /// <example>41.7</example>
    [Range(0.01, 1000, ErrorMessage = "Quantity must be greater than zero and no more than 1000 kilograms.")]
    public decimal QuantityKg { get; set; }
}
