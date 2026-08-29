using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MccIntakeService.Api.Infrastructure;

/// <summary>Names for the interim authentication scheme.</summary>
public static class IntakeAuthentication
{
    /// <summary>The scheme the service authenticates with until SCRUM-34 lands.</summary>
    public const string Scheme = "IntakeRoleHeader";

    /// <summary>Header carrying the caller's role, e.g. <c>MccManager</c>.</summary>
    public const string RoleHeader = "X-Intake-Role";

    /// <summary>Header carrying the caller's name, used for audit fields when present.</summary>
    public const string UserHeader = "X-Intake-User";
}

/// <summary>
/// Reads the caller's role straight off a request header and turns it into a
/// <see cref="ClaimsPrincipal"/>, so the authorization policies SCRUM-51 requires can be enforced
/// before a real identity provider exists.
/// </summary>
/// <remarks>
/// <para>
/// This trusts the caller completely and is <em>not</em> a security control. It exists so that the
/// authorization side of the service — policies, <c>[Authorize]</c> attributes, the 401/403
/// contract and the tests covering them — is built and proven now, rather than bolted on later.
/// </para>
/// <para>
/// SCRUM-34 replaces this handler with real authentication. That change is confined to the
/// registration in <c>Program.cs</c>: the scheme name, the policies in
/// <see cref="IntakePolicies"/> and the attributes on the controllers all stay put, because a
/// JWT bearer handler produces the same role claims this one does.
/// </para>
/// <para>
/// The handler is registered only outside Production, and start-up fails if that is ever
/// attempted, so a placeholder cannot silently become the production gate.
/// </para>
/// </remarks>
public sealed class IntakeRoleHeaderHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public IntakeRoleHeaderHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(IntakeAuthentication.RoleHeader, out var roles))
        {
            // No result rather than a failure: an anonymous caller is legitimate on the read
            // endpoints, and only the [Authorize] endpoints turn this into a 401 challenge.
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, Request.Headers[IntakeAuthentication.UserHeader].ToString() is { Length: > 0 } name
                ? name
                : "unknown")
        };

        // A caller may hold several roles; the header carries them comma-separated.
        claims.AddRange(roles
            .ToString()
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(role => new Claim(ClaimTypes.Role, role)));

        var identity = new ClaimsIdentity(claims, IntakeAuthentication.Scheme);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), IntakeAuthentication.Scheme);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
