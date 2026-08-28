using MccIntakeService.Domain.Societies;

namespace MccIntakeService.Application.Consignments;

/// <summary>
/// Allocates the next consignment reference for a society on a given intake date (SCRUM-6).
/// </summary>
public interface IConsignmentReferenceGenerator
{
    /// <summary>
    /// Produces the next reference of the form MCC-YYYYMMDD-SOCIETY-NN. The value is only a
    /// candidate: uniqueness is settled by the unique index when the consignment is saved.
    /// </summary>
    Task<string> NextAsync(Society society, DateOnly arrivalDate, CancellationToken cancellationToken = default);
}
