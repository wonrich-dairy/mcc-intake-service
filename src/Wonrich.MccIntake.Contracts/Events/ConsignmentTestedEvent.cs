namespace Wonrich.MccIntake.Contracts.Events;

/// <summary>
/// Kafka topics the MCC and Intake service publishes on.
/// </summary>
public static class MccIntakeTopics
{
    /// <summary>One message per gate verdict, accept and reject alike (SCRUM-141).</summary>
    public const string ConsignmentTested = "wonrich.mcc.consignment-tested.v1";
}

/// <summary>
/// Published on <c>wonrich.mcc.consignment-tested.v1</c> each time an officer records a gate
/// verdict, accepted or rejected (SCRUM-141). Rejected milk never leaves the MCC, so this is the
/// only way it reaches society performance reporting.
/// Same envelope conventions as Processing and Quality Lab: camelCase JSON, headers
/// x-correlation-id / eventId / eventType / schemaVersion, at-least-once delivery (consumers
/// dedupe on eventId). The message key is the society code, so one society's events stay in order.
/// Breaking change = new topic .v2.
/// </summary>
public sealed record ConsignmentTestedEvent
{
    public const string Type = "ConsignmentTested";

    /// <summary>Always "ConsignmentTested"; also sent as the eventType header.</summary>
    public string EventType => Type;

    /// <summary>Unique ID for consumer-side deduplication (the relay is at-least-once).</summary>
    public Guid EventId { get; init; } = Guid.NewGuid();

    public string SchemaVersion { get; init; } = "v1";

    public string CorrelationId { get; init; } = string.Empty;

    /// <summary>The consignment's reference, e.g. KC-20260919-003.</summary>
    public string ConsignmentReference { get; init; } = string.Empty;

    public SocietyDetails Society { get; init; } = new();

    /// <summary>The cans on the consignment, as labelled at the gate.</summary>
    public IReadOnlyList<CanDetails> Cans { get; init; } = Array.Empty<CanDetails>();

    public decimal QuantityLitres { get; init; }

    /// <summary>Every reading and derived value of the gate panel.</summary>
    public QualityPanelDetails Panel { get; init; } = new();

    /// <summary>"Accept" or "Reject".</summary>
    public string Verdict { get; init; } = string.Empty;

    /// <summary>The parameter that failed, on a rejection.</summary>
    public string? FailedParameter { get; init; }

    /// <summary>That parameter's value, on a rejection.</summary>
    public string? FailedValue { get; init; }

    /// <summary>
    /// Accepted, but with a measure close to its limit (MarginToThreshold within the configured
    /// margin). Always false on a rejection.
    /// </summary>
    public bool IsMarginal { get; init; }

    /// <summary>
    /// When the officer recorded the verdict. For a record captured offline this is the time it
    /// was taken, not the time it was synced.
    /// </summary>
    public DateTime OccurredAtUtc { get; init; }
}

public sealed record SocietyDetails
{
    public string Code { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;
}

public sealed record CanDetails
{
    /// <summary>As painted on the can, e.g. "KC 01".</summary>
    public string Label { get; init; } = string.Empty;

    public decimal QuantityLitres { get; init; }
}

public sealed record QualityPanelDetails
{
    public decimal FatPercent { get; init; }

    public decimal RawLactometerReading { get; init; }

    public decimal TemperatureCelsius { get; init; }

    public decimal WaterPercent { get; init; }

    public string KqColour { get; init; } = string.Empty;

    public bool SmellOk { get; init; }

    public bool ColourOk { get; init; }

    public bool TasteOk { get; init; }

    public decimal CorrectedClr { get; init; }

    public decimal Snf { get; init; }

    public decimal TotalSolids { get; init; }

    public string StabilityGrade { get; init; } = string.Empty;

    public string PassedAlcoholAt { get; init; } = string.Empty;
}
