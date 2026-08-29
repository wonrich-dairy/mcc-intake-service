namespace MccIntakeService.Api.Infrastructure;

/// <summary>
/// Roles the service recognises. SCRUM-51 requires that only MCC Managers and System
/// Administrators may create or edit societies.
/// </summary>
/// <remarks>
/// These names back <see cref="IntakePolicies.ManageSocieties"/>, which is enforced on the write
/// endpoints of <see cref="Controllers.SocietiesController"/>. The identity carrying them is
/// currently established by <see cref="IntakeRoleHeaderHandler"/>, a placeholder that SCRUM-34
/// replaces with real authentication; the role names and the policy survive that change.
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
