namespace MccIntakeService.Application.Societies;

/// <summary>Details supplied when registering a new supplying society (SCRUM-51).</summary>
public sealed record CreateSocietyCommand(
    string Code,
    string Name,
    string CanLabelPrefix,
    string? ContactPerson = null,
    string? ContactNumber = null);

/// <summary>
/// Details supplied when amending a society. The code is only moved when consignments do not
/// yet exist against the society; everything else is always amendable.
/// </summary>
public sealed record UpdateSocietyCommand(
    string Code,
    string Name,
    string CanLabelPrefix,
    string? ContactPerson = null,
    string? ContactNumber = null);

/// <summary>Field a society listing can be ordered by.</summary>
public enum SocietySortBy
{
    Code = 0,
    Name = 1,
    IsActive = 2
}

/// <summary>Search and ordering options for the society list (SCRUM-51).</summary>
public sealed record SocietyQuery
{
    /// <summary>Free-text fragment matched against both name and code.</summary>
    public string? Search { get; init; }

    /// <summary>When false, retired societies are listed alongside active ones.</summary>
    public bool ActiveOnly { get; init; } = true;

    public SocietySortBy SortBy { get; init; } = SocietySortBy.Code;

    public bool Descending { get; init; }
}
