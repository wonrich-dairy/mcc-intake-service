using System.ComponentModel.DataAnnotations;

namespace MccIntakeService.Models;

/// <summary>
/// Represents a Milk Collection Center (MCC) registered in the system.
/// </summary>
public class MilkCollectionCenter
{
    /// <summary>Primary key.</summary>
    public int Id { get; set; }

    /// <summary>Unique MCC code (e.g. "MCC-001").</summary>
    [Required]
    [MaxLength(20)]
    public string Code { get; set; } = string.Empty;

    /// <summary>Display name of the collection center.</summary>
    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Physical location / address.</summary>
    [MaxLength(500)]
    public string? Location { get; set; }

    /// <summary>Contact phone number.</summary>
    [MaxLength(20)]
    public string? ContactNumber { get; set; }

    /// <summary>Whether this MCC is currently active.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Date the MCC was registered.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Last updated timestamp.</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
