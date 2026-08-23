namespace MccIntakeService.Api.Infrastructure;

/// <summary>
/// Roles the service recognises. SCRUM-51 requires that only MCC Managers and System
/// Administrators may create or edit societies.
/// </summary>
/// <remarks>
/// These names are declared here, and the endpoints that need them are annotated in Swagger,
/// but nothing is <em>enforced</em> yet: enforcement needs an authentication scheme, which is
/// SCRUM-34. When that lands, registering the scheme and applying
/// <c>[Authorize(Policy = IntakePolicies.ManageSocieties)]</c> to the write endpoints on
/// <see cref="Controllers.SocietiesController"/> is the whole change — the role names and the
/// documented contract do not move.
/// </remarks>
public static class IntakeRoles
{
    public const string SystemAdministrator = "SystemAdministrator";

    public const string MccManager = "MccManager";

    public const string IntakeOfficer = "IntakeOfficer";
}

/// <summary>Named authorization policies, paired with the roles that satisfy them.</summary>
public static class IntakePolicies
{
    /// <summary>Create, amend, deactivate or reactivate a supplying society (SCRUM-51).</summary>
    public const string ManageSocieties = "ManageSocieties";

    /// <summary>Roles that satisfy <see cref="ManageSocieties"/>.</summary>
    public static readonly IReadOnlyList<string> ManageSocietiesRoles =
    [
        IntakeRoles.SystemAdministrator,
        IntakeRoles.MccManager
    ];
}
