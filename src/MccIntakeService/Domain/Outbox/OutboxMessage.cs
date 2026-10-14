namespace MccIntakeService.Domain.Outbox;

/// <summary>
/// Transactional outbox row (SCRUM-141). Written in the same SaveChanges as the gate verdict, then
/// relayed to Kafka by OutboxRelayService, so a rollback publishes nothing and a broker outage
/// never fails the officer's submission. Same shape as the Processing and Quality Lab outboxes.
/// </summary>
public class OutboxMessage
{
    public Guid Id { get; set; }

    public string Topic { get; set; } = string.Empty;

    public string Key { get; set; } = string.Empty;

    public string EventType { get; set; } = string.Empty;

    public string Payload { get; set; } = string.Empty;

    public string HeadersJson { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? ProcessedAtUtc { get; set; }

    public int RetryCount { get; set; }

    public string? LastError { get; set; }

    /// <summary>Pending, Processed, or Poisoned (past the retry budget; needs human review).</summary>
    public string Status { get; set; } = OutboxStatus.Pending;

    public string CorrelationId { get; set; } = string.Empty;
}

/// <summary>The states an outbox row moves through.</summary>
public static class OutboxStatus
{
    public const string Pending = "Pending";
    public const string Processed = "Processed";
    public const string Poisoned = "Poisoned";
}
