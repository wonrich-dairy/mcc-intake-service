using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Wonrich.Auth;

/// <summary>
/// The browser origin policy every Wonrich service serves the SPA under (SCRUM-92).
/// </summary>
/// <remarks>
/// <para>
/// The client is a separate origin from every service it calls, so each one answers its own
/// preflight. Without the middleware in the pipeline the preflight is not short-circuited ahead of
/// endpoint selection: <c>OPTIONS</c> reaches MVC, which has no action for it, and the browser
/// reports a failure carrying no status and no headers. The SPA cannot tell that apart from an
/// unreachable service, so it blamed the officer's network.
/// </para>
/// <para>
/// Origins are configuration rather than code. Both services named
/// <c>http://localhost:5173</c> in <c>Program.cs</c>, which is the dev server and nothing else, so
/// the preflight would have been refused in staging and production the first time a browser called
/// either of them — the same outage the original fix was for, moved to an environment where it is
/// harder to see.
/// </para>
/// <para>
/// Credentials are not allowed: the token travels in the <c>Authorization</c> header, not a
/// cookie, so nothing needs them and allowing them widens what a hostile page can attempt.
/// </para>
/// </remarks>
public static class WonrichCorsExtensions
{
    /// <summary>Name of the policy applied to every route.</summary>
    public const string PolicyName = "frontend";

    /// <summary>Configuration section listing the browser origins allowed to call the service.</summary>
    public const string SectionName = "Cors";

    /// <summary>
    /// Adds the <see cref="PolicyName"/> policy, allowing the origins listed under
    /// <c>Cors:AllowedOrigins</c>. An empty list allows none, which is the safe default for an
    /// environment that has not been told about its client yet.
    /// </summary>
    public static IServiceCollection AddWonrichCors(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        // Blank entries are dropped rather than registered: an environment variable set to an
        // empty string would otherwise become an origin no browser can ever match, and hide the
        // fact that the service was never told about its client.
        var origins = (configuration.GetSection($"{SectionName}:AllowedOrigins").Get<string[]>() ?? [])
            .Where(origin => !string.IsNullOrWhiteSpace(origin))
            .ToArray();

        return services.AddCors(options => options.AddPolicy(PolicyName, policy =>
        {
            if (origins.Length == 0)
            {
                return;
            }

            policy
                .WithOrigins(origins)
                .AllowAnyHeader()
                .AllowAnyMethod();
        }));
    }

    /// <summary>
    /// Puts the policy in the pipeline. Call before authentication: a preflight carries no
    /// <c>Authorization</c> header, so it has to be answered before anything tries to authenticate it.
    /// </summary>
    public static IApplicationBuilder UseWonrichCors(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.UseCors(PolicyName);
    }
}
