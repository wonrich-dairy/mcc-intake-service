namespace MccIntakeService.Application.Consignments;

/// <summary>Registration and retrieval of society consignments at the chilling centre (SCRUM-6).</summary>
public interface IConsignmentService
{
    /// <summary>Registers an arriving consignment and returns the stored record.</summary>
    Task<ConsignmentView> RegisterAsync(
        RegisterConsignmentCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>Fetches a consignment by its MCC-YYYYMMDD-SOCIETY-NN reference.</summary>
    Task<ConsignmentView?> GetByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>Lists consignments matching the supplied society, date or reference filters.</summary>
    Task<PagedResult<ConsignmentView>> SearchAsync(
        ConsignmentQuery query,
        CancellationToken cancellationToken = default);
}
