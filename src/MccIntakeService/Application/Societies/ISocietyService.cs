namespace MccIntakeService.Application.Societies;

/// <summary>
/// Management of supplying societies and their can labels (SCRUM-51). Societies are never
/// deleted — they are deactivated, so historical consignments keep resolving to their source.
/// </summary>
public interface ISocietyService
{
    /// <summary>Lists societies, optionally filtered by a name or code fragment and reordered.</summary>
    Task<IReadOnlyList<SocietyView>> ListAsync(
        SocietyQuery? query = null,
        CancellationToken cancellationToken = default);

    /// <summary>Fetches a single society by its identifier.</summary>
    Task<SocietyView?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Registers a new society. The code must not already be in use.</summary>
    Task<SocietyView> CreateAsync(CreateSocietyCommand command, CancellationToken cancellationToken = default);

    /// <summary>
    /// Amends a society. Moving the code is refused once consignments exist against it.
    /// </summary>
    Task<SocietyView> UpdateAsync(
        Guid id,
        UpdateSocietyCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>Retires a society so it can no longer be selected for new consignments.</summary>
    Task<SocietyView> DeactivateAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Returns a retired society to service.</summary>
    Task<SocietyView> ReactivateAsync(Guid id, CancellationToken cancellationToken = default);
}

/// <summary>A society as offered to the officer for selection and to the manager for editing.</summary>
public sealed record SocietyView(
    Guid Id,
    string Code,
    string Name,
    string CanLabelPrefix,
    string? ContactPerson,
    string? ContactNumber,
    bool IsActive);
