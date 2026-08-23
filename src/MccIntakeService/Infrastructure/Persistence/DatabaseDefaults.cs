using Microsoft.EntityFrameworkCore;

namespace MccIntakeService.Infrastructure.Persistence;

/// <summary>Shared database settings used by both the running host and the design-time tooling.</summary>
public static class DatabaseDefaults
{
    /// <summary>Configuration key holding the MySQL version the schema is generated against.</summary>
    public const string ServerVersionKey = "Database:ServerVersion";

    /// <summary>Version assumed when nothing is configured; matches the container used locally.</summary>
    public const string DefaultServerVersion = "8.0.36-mysql";

    /// <summary>Connection string used only when scaffolding migrations without a live server.</summary>
    public const string DesignTimeConnectionString =
        "Server=localhost;Port=3307;Database=mcc_intake;User=mcc_user;Password=DesignTimeOnly";

    /// <summary>Reads the configured MySQL server version, falling back to <see cref="DefaultServerVersion"/>.</summary>
    public static ServerVersion ServerVersionFrom(IConfiguration configuration)
    {
        var configured = configuration[ServerVersionKey];

        return ServerVersion.Parse(string.IsNullOrWhiteSpace(configured) ? DefaultServerVersion : configured);
    }
}
