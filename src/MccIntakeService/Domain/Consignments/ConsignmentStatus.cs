namespace MccIntakeService.Domain.Consignments;

/// <summary>
/// Lifecycle of a consignment at the chilling centre. SCRUM-6 only produces
/// <see cref="Registered"/>; gate testing moves it onward (SCRUM-7).
/// </summary>
public enum ConsignmentStatus
{
    /// <summary>Recorded at the gate, awaiting quality testing.</summary>
    Registered = 0,

    /// <summary>Passed gate testing and accepted into the centre.</summary>
    Accepted = 1,

    /// <summary>Failed gate testing and turned away.</summary>
    Rejected = 2
}
