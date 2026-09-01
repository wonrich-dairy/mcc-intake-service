using System.Security.Claims;
using Wonrich.Auth.Tokens;

namespace MccIntakeService.Api.Infrastructure;

/// <summary>
/// How an officer is stamped onto the records they create.
/// </summary>
/// <remarks>
/// Every record that names who took it — a gate panel, a pour, a dispatch note, a screening —
/// reads back through the batch trace (SCRUM-12), where the point is for a person to be able to
/// go and ask that officer what they saw. So the sign-in name is what gets stored, not the
/// subject id. Written the other way round it never fell through to the name at all: the subject
/// claim is on every token this service accepts, so the fallback could not fire and every field
/// carried an opaque GUID.
/// </remarks>
public static class OfficerIdentityExtensions
{
    /// <summary>The officer's sign-in name, falling back to their id if a token carries no name.</summary>
    public static string? OfficerIdentity(this ClaimsPrincipal principal) =>
        principal.UserName() ?? principal.UserId();
}
