namespace MccIntakeService.Infrastructure.Persistence;

/// <summary>Shared database settings used by both the running host and the design-time tooling.</summary>
public static class DatabaseDefaults
{
    /// <summary>Connection string used only when scaffolding migrations without a live server.</summary>
    public const string DesignTimeConnectionString =
        "Server=localhost;Port=3307;Database=mcc_intake;User=mcc_user;Password=DesignTimeOnly";
}
