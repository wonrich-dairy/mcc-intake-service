using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Wonrich.Auth.Tokens;

namespace Wonrich.Auth;

/// <summary>
/// Registers the shared Wonrich authentication for a service (SCRUM-34). Every service validates
/// tokens on its own — signature, issuer, audience and expiry — so none of them has to call the
/// auth service to decide whether a request is authentic.
/// </summary>
public static class WonrichAuthExtensions
{
    /// <summary>
    /// Binds <see cref="WonrichJwtOptions"/> from configuration and registers JWT bearer
    /// authentication against it. Start-up fails if the settings are missing or a weak signing
    /// key is configured, rather than the service coming up unable to authenticate anyone.
    /// </summary>
    public static IServiceCollection AddWonrichAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<WonrichJwtOptions>()
            .Bind(configuration.GetSection(WonrichJwtOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.TryAddTimeProvider();
        services.AddSingleton<IAccessTokenIssuer, AccessTokenIssuer>();

        var options = new WonrichJwtOptions();
        configuration.GetSection(WonrichJwtOptions.SectionName).Bind(options);

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(bearer =>
            {
                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = options.Issuer,
                    ValidateAudience = true,
                    ValidAudience = options.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = AccessTokenIssuer.SigningKeyFor(options),
                    ValidateLifetime = true,
                    ClockSkew = options.ClockSkew,
                    RoleClaimType = ClaimTypes.Role,
                    NameClaimType = ClaimTypes.Name
                };

                bearer.Events = new JwtBearerEvents
                {
                    OnAuthenticationFailed = context =>
                    {
                        // Logged at warning, without the token itself: a rejected token is worth
                        // noticing, and echoing it into the log would store a live credential.
                        context.HttpContext
                            .RequestServices
                            .GetRequiredService<ILoggerFactory>()
                            .CreateLogger("Wonrich.Auth")
                            .LogWarning(
                                "Token rejected for {Method} {Path} from {Source}: {Reason}",
                                context.Request.Method,
                                context.Request.Path,
                                context.HttpContext.Connection.RemoteIpAddress,
                                context.Exception.GetType().Name);

                        return Task.CompletedTask;
                    }
                };
            });

        return services;
    }

    /// <summary>
    /// Registers the authorization policies every Wonrich service shares, then lets the caller
    /// add its own. Unauthorised calls answer 403 through the standard pipeline.
    /// </summary>
    public static IServiceCollection AddWonrichAuthorization(
        this IServiceCollection services,
        Action<AuthorizationPolicyRegistry>? configure = null)
    {
        var registry = new AuthorizationPolicyRegistry();
        configure?.Invoke(registry);

        var builder = services.AddAuthorizationBuilder();

        foreach (var (name, roles) in registry.Policies)
        {
            builder.AddPolicy(name, policy => policy
                .RequireAuthenticatedUser()
                .RequireRole(roles));
        }

        return services;
    }

    private static void TryAddTimeProvider(this IServiceCollection services)
    {
        if (services.All(descriptor => descriptor.ServiceType != typeof(TimeProvider)))
        {
            services.AddSingleton(TimeProvider.System);
        }
    }
}

/// <summary>Collects the role-backed policies a service wants registered.</summary>
public sealed class AuthorizationPolicyRegistry
{
    private readonly Dictionary<string, string[]> _policies = [];

    internal IReadOnlyDictionary<string, string[]> Policies => _policies;

    /// <summary>Declares a policy satisfied by any one of the given roles.</summary>
    public AuthorizationPolicyRegistry Add(string name, params string[] roles)
    {
        _policies[name] = roles;

        return this;
    }
}
