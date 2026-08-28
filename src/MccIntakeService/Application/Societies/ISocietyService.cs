namespace MccIntakeService.Application.Societies;

/// <summary>
/// Read-only access to registered societies, so the intake officer can pick one rather than
/// type it. Creating and editing societies is owned by SCRUM-51.
/// </summary>
public interface ISocietyService
{
    /// <summary>Lists societies available for selection at the gate.</summary>
    Task<IReadOnlyList<SocietyView>> ListAsync(bool activeOnly = true, CancellationToken cancellationToken = default);

    /// <summary>Fetches a single society by its identifier.</summary>
    Task<SocietyView?> GetAsync(Guid id, CancellationToken cancellationToken = default);
}

/// <summary>A society as offered to the officer for selection.</summary>
public sealed record SocietyView(Guid Id, string Code, string Name, string CanLabelPrefix, bool IsActive);
