using MccIntakeService.Domain.Tanks;

namespace MccIntakeService.Infrastructure.Persistence.Configurations;

/// <summary>
/// The centre's three chilling tanks (SCRUM-52). They are plant, not reference data an officer
/// maintains, so they ship with the schema and there is no API to add or remove them. Ids are
/// fixed so the seed stays idempotent across migrations.
/// </summary>
internal static class ChillingTankSeed
{
    public static readonly IReadOnlyList<ChillingTank> Tanks =
    [
        new ChillingTank(Guid.Parse("9a1c2b30-0001-4d5e-8f60-000000000001"), "T1", "Chilling Tank 1", 5000m),
        new ChillingTank(Guid.Parse("9a1c2b30-0002-4d5e-8f60-000000000002"), "T2", "Chilling Tank 2", 5000m),
        new ChillingTank(Guid.Parse("9a1c2b30-0003-4d5e-8f60-000000000003"), "T3", "Chilling Tank 3", 3000m)
    ];
}
