using MccIntakeService.Domain.Consignments;

namespace MccIntakeService.Application.Consignments;

/// <summary>Request to register an arriving consignment at the gate.</summary>
/// <param name="SocietyId">Identifier of a society already registered in the system.</param>
/// <param name="Cans">The cans delivered, at least one.</param>
/// <param name="ArrivalAtLocal">
/// Arrival wall-clock time at the centre. When omitted the current time is captured automatically;
/// the officer may correct it before submitting, but not after.
/// </param>
/// <param name="RegisteredBy">Intake officer identifier, when authentication is available.</param>
public sealed record RegisterConsignmentCommand(
    Guid SocietyId,
    IReadOnlyCollection<CanEntry> Cans,
    DateTime? ArrivalAtLocal = null,
    string? RegisteredBy = null);

/// <summary>Filters for locating registered consignments by society, by date, or by reference.</summary>
public sealed record ConsignmentQuery
{
    public Guid? SocietyId { get; init; }

    public string? SocietyCode { get; init; }

    public string? Reference { get; init; }

    public DateOnly? ArrivalDate { get; init; }

    public DateOnly? FromDate { get; init; }

    public DateOnly? ToDate { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 25;
}

/// <summary>
/// One can as recorded against a consignment. Weight is what the gate measured; litres are
/// derived from it at registration.
/// </summary>
public sealed record ConsignmentCanView(
    string CanLabel,
    int CanNumber,
    decimal QuantityKg,
    decimal QuantityLitres);

/// <summary>Full detail of a registered consignment, including its can breakdown.</summary>
public sealed record ConsignmentView(
    Guid Id,
    string Reference,
    Guid SocietyId,
    string SocietyCode,
    string SocietyName,
    DateTime ArrivalAtLocal,
    DateOnly ArrivalDate,
    ConsignmentStatus Status,
    decimal TotalQuantityKg,
    decimal TotalQuantityLitres,
    int CanCount,
    DateTime RegisteredAtUtc,
    string? RegisteredBy,
    IReadOnlyList<ConsignmentCanView> Cans);

/// <summary>A page of results together with the total number of matches.</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}
