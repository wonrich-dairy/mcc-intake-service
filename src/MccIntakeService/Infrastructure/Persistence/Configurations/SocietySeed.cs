using MccIntakeService.Domain.Societies;

namespace MccIntakeService.Infrastructure.Persistence.Configurations;

/// <summary>
/// Reference societies shipped with the schema so intake can be exercised on a fresh database.
/// Ids are fixed so the seed is idempotent across migrations. Managers add and retire societies
/// through the API from here on (SCRUM-51).
/// </summary>
internal static class SocietySeed
{
    public static readonly IReadOnlyList<Society> Societies =
    [
        new Society(
            Guid.Parse("6f0f6f1a-0001-4a2b-9c3d-000000000001"),
            "KC",
            "Kandy Co-operative Dairy Society",
            "KC",
            "Sunil Perera",
            "+94 81 222 3344"),
        new Society(
            Guid.Parse("6f0f6f1a-0002-4a2b-9c3d-000000000002"),
            "MT",
            "Matale Farmers' Milk Society",
            "MT",
            "Kamala Ranasinghe",
            "+94 66 222 5566"),
        new Society(
            Guid.Parse("6f0f6f1a-0003-4a2b-9c3d-000000000003"),
            "NW",
            "Nuwara Eliya Highland Society",
            "NW",
            "Ravi Kumar",
            "+94 52 222 7788"),
        new Society(
            Guid.Parse("6f0f6f1a-0004-4a2b-9c3d-000000000004"),
            "BD",
            "Badulla Uva Milk Society",
            "BD",
            "Anoma Jayasuriya",
            "+94 55 222 9900")
    ];
}
