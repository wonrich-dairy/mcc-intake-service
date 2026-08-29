using Microsoft.AspNetCore.Mvc;

namespace MccIntakeService.Api.Contracts;

/// <summary>
/// The problem+json body this service returns when a request breaks a domain rule.
/// </summary>
/// <remarks>
/// Extends the RFC 9457 members with the fields the service actually writes, so the published
/// contract matches the response. Documenting the plain <see cref="ProblemDetails"/> would hide
/// <see cref="Code"/>, which is the only field a consumer can branch on (SCRUM-55).
/// </remarks>
public class IntakeProblemDetails : ProblemDetails
{
    /// <summary>
    /// Stable, machine-readable identifier for the rule that was broken. Branch on this rather
    /// than on <see cref="ProblemDetails.Detail"/>, which is prose meant for a human.
    /// </summary>
    /// <example>intake_cutoff_exceeded</example>
    public string Code { get; set; } = string.Empty;

    /// <summary>Correlation id for the request, for matching against the service logs.</summary>
    /// <example>00-bb3d70a77542dbcf0a1fea2efa087d58-1a11948821f5c4ea-00</example>
    public string? TraceId { get; set; }
}

/// <summary>
/// The 422 body. Two distinct failures share this status, and <see cref="IntakeProblemDetails.Code"/>
/// is what separates them:
/// <list type="bullet">
/// <item>
/// <description>
/// <c>intake_cutoff_exceeded</c> — milk arrived after the centre closed for the day.
/// <see cref="Cutoff"/> and <see cref="ArrivalTime"/> are populated.
/// </description>
/// </item>
/// <item>
/// <description>
/// <c>entity_not_found</c> — the request references a society that is not registered.
/// <see cref="Cutoff"/> and <see cref="ArrivalTime"/> are absent.
/// </description>
/// </item>
/// </list>
/// </summary>
public sealed class IntakeUnprocessableProblemDetails : IntakeProblemDetails
{
    /// <summary>
    /// Configured local time after which intake closes, as HH:mm.
    /// Present only when <see cref="IntakeProblemDetails.Code"/> is <c>intake_cutoff_exceeded</c>.
    /// </summary>
    /// <example>16:00</example>
    public string? Cutoff { get; set; }

    /// <summary>
    /// Local time of day the consignment arrived, as HH:mm.
    /// Present only when <see cref="IntakeProblemDetails.Code"/> is <c>intake_cutoff_exceeded</c>.
    /// </summary>
    /// <example>21:13</example>
    public string? ArrivalTime { get; set; }
}

/// <summary>
/// The 409 body, returned when a submitted code is already in use (SCRUM-51).
/// <see cref="IntakeProblemDetails.Code"/> is always <c>duplicate_code</c>.
/// </summary>
public sealed class DuplicateCodeProblemDetails : IntakeProblemDetails
{
    /// <summary>The code that was already taken.</summary>
    /// <example>KC</example>
    public string ConflictingCode { get; set; } = string.Empty;
}
