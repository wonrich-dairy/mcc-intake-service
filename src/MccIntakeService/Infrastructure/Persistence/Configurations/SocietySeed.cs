using MccIntakeService.Domain.Societies;

namespace MccIntakeService.Infrastructure.Persistence.Configurations;

/// <summary>
/// Reference societies shipped with the schema so intake can be exercised before SCRUM-51
/// delivers society management. Ids are fixed so the seed is idempotent across migrations.
/// </summary>
internal static class SocietySeed
{
    public static readonly IReadOnlyList<Society> Societies =
    [
        new Society(Guid.Parse("6f0f6f1a-0001-4a2b-9c3d-000000000001"), "KC", "Kandy Co-operative Dairy Society", "KC"),
        new Society(Guid.Parse("6f0f6f1a-0002-4a2b-9c3d-000000000002"), "MT", "Matale Farmers' Milk Society", "MT"),
        new Society(Guid.Parse("6f0f6f1a-0003-4a2b-9c3d-000000000003"), "NW", "Nuwara Eliya Highland Society", "NW"),
        new Society(Guid.Parse("6f0f6f1a-0004-4a2b-9c3d-000000000004"), "BD", "Badulla Uva Milk Society", "BD")
    ];
}
