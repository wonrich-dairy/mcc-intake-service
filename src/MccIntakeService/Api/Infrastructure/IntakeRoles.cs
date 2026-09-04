namespace MccIntakeService.Api.Infrastructure;

/// <summary>
/// Named authorization policies this service enforces, paired with the shared Wonrich roles that
/// satisfy them (SCRUM-34).
/// </summary>
/// <remarks>
/// The roles themselves live in <see cref="Wonrich.Auth.Authorization.WonrichRoles"/> because all
/// four services write their policies against the same vocabulary. What belongs here is only the
/// mapping this service chooses: which of those roles may do what at the chilling centre. The
/// pairing is registered in <c>Program.cs</c>.
/// </remarks>
public static class IntakePolicies
{
    /// <summary>Create, amend, deactivate or reactivate a supplying society (SCRUM-51).</summary>
    public const string ManageSocieties = "ManageSocieties";

    /// <summary>Register an arriving consignment at the gate (SCRUM-6).</summary>
    public const string RegisterConsignments = "RegisterConsignments";

    /// <summary>Record the quality test panel and its verdict at the gate (SCRUM-7).</summary>
    public const string RecordQualityTests = "RecordQualityTests";

    /// <summary>Pour an accepted consignment into a chilling tank (SCRUM-52).</summary>
    public const string PourToTanks = "PourToTanks";

    /// <summary>Add a chilling tank, rename one, or take one out of service (SCRUM-52).</summary>
    public const string ManageTanks = "ManageTanks";

    /// <summary>Record a bowser dispatch note (SCRUM-8).</summary>
    public const string RecordDispatchNotes = "RecordDispatchNotes";

    /// <summary>Screen an arriving bowser and create the batch at factory intake (SCRUM-9).</summary>
    public const string ScreenFactoryArrivals = "ScreenFactoryArrivals";

    /// <summary>
    /// Resolve a batch back to its source tanks and consignments (SCRUM-12). The story calls for
    /// service-to-service authentication; with a single shared token scheme that is a token issued
    /// to a service account holding one of these roles, not a separate mechanism.
    /// </summary>
    public const string TraceBatches = "TraceBatches";
}
