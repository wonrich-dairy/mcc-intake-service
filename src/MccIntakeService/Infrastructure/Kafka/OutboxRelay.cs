using System.Text.Json;
using MccIntakeService.Domain.Outbox;
using MccIntakeService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MccIntakeService.Infrastructure.Kafka;

/// <summary>Settings for the outbox relay, bound from the "Outbox" section.</summary>
public sealed class OutboxOptions
{
    public const string SectionName = "Outbox";

    /// <summary>Publish attempts before a row is Poisoned and parked on the dead-letter topic.</summary>
    public int MaxRetries { get; set; } = 20;

    /// <summary>Rows relayed per poll.</summary>
    public int BatchSize { get; set; } = 20;

    /// <summary>Where a row goes once its retries are spent.</summary>
    public string DeadLetterTopic { get; set; } = "wonrich.dlq.mcc-consignment-tested.v1";
}

/// <summary>
/// One pass over outbox_messages: publishes the oldest pending rows in order and records the
/// outcome (SCRUM-141; same algorithm as Processing SCRUM-68 and Quality Lab SCRUM-24).
/// - On MySQL rows are claimed with SELECT ... FOR UPDATE SKIP LOCKED, so two instances never
///   publish the same row.
/// - A batch stops at the first failure, which keeps each society's events in order.
/// - A row that fails MaxRetries times is Poisoned and a copy parked on the dead-letter topic.
/// At-least-once: consumers dedupe on eventId.
/// </summary>
public sealed class OutboxRelay(OutboxOptions options, TimeProvider time, ILogger<OutboxRelay> logger)
{
    /// <returns>False when a non-empty batch published nothing, so the caller backs off.</returns>
    public async Task<bool> ProcessBatchAsync(MccIntakeDbContext db, IKafkaProducer producer, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var pending = await ClaimAsync(db, ct);
        if (pending.Count == 0)
        {
            await tx.RollbackAsync(CancellationToken.None);
            return true;
        }

        foreach (var message in pending)
        {
            var published = await producer.PublishAsync(
                message.Topic, message.Key, message.Payload, message.HeadersJson, message.CorrelationId, ct);

            if (published)
            {
                message.Status = OutboxStatus.Processed;
                message.ProcessedAtUtc = time.GetUtcNow().UtcDateTime;
                message.LastError = null;
                continue;
            }

            message.RetryCount++;
            message.LastError = $"Publish failed, attempt {message.RetryCount} of {options.MaxRetries}";

            if (message.RetryCount >= options.MaxRetries)
            {
                message.Status = OutboxStatus.Poisoned;
                message.ProcessedAtUtc = time.GetUtcNow().UtcDateTime;
                logger.LogError("Outbox message {Id} key={Key} failed {MaxRetries} times and is Poisoned; it needs review",
                    message.Id, message.Key, options.MaxRetries);
                await ParkOnDeadLetterAsync(producer, message, ct);
            }

            // Stop at the first failure: a later event for the same society must not overtake it.
            break;
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return pending.Any(message => message.Status == OutboxStatus.Processed);
    }

    private async Task<List<OutboxMessage>> ClaimAsync(MccIntakeDbContext db, CancellationToken ct)
    {
        if (db.Database.ProviderName?.Contains("MySql", StringComparison.OrdinalIgnoreCase) == true)
        {
            return await db.OutboxMessages.FromSqlInterpolated($@"
SELECT * FROM outbox_messages
WHERE Status = {OutboxStatus.Pending}
ORDER BY CreatedAtUtc
LIMIT {options.BatchSize}
FOR UPDATE SKIP LOCKED").ToListAsync(ct);
        }

        // SQLite in the tests: a single connection, so there is nothing to lock against.
        return await db.OutboxMessages
            .Where(message => message.Status == OutboxStatus.Pending)
            .OrderBy(message => message.CreatedAtUtc)
            .Take(options.BatchSize)
            .ToListAsync(ct);
    }

    private async Task ParkOnDeadLetterAsync(IKafkaProducer producer, OutboxMessage message, CancellationToken ct)
    {
        Dictionary<string, string> headers;
        try
        {
            headers = JsonSerializer.Deserialize<Dictionary<string, string>>(message.HeadersJson) ?? [];
        }
        catch (JsonException)
        {
            headers = [];
        }

        headers["x-original-topic"] = message.Topic;
        headers["x-failure-reason"] = message.LastError ?? "retries exhausted";
        headers["x-failure-timestamp"] = time.GetUtcNow().UtcDateTime.ToString("o");

        // Best effort: if the broker is still down this fails too, which is why the row stays
        // Poisoned in the outbox as the durable record of the failure.
        if (!await producer.PublishAsync(options.DeadLetterTopic, message.Key, message.Payload,
                JsonSerializer.Serialize(headers), message.CorrelationId, ct))
        {
            logger.LogError("Outbox message {Id} could not be parked on {DeadLetterTopic}; it remains Poisoned in outbox_messages",
                message.Id, options.DeadLetterTopic);
        }
    }
}

/// <summary>Runs <see cref="OutboxRelay"/> every few seconds, backing off while the broker is down.</summary>
public sealed class OutboxRelayService(IServiceProvider services, OutboxRelay relay, ILogger<OutboxRelayService> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var failedBatches = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            var ok = true;
            try
            {
                using var scope = services.CreateScope();
                ok = await relay.ProcessBatchAsync(
                    scope.ServiceProvider.GetRequiredService<MccIntakeDbContext>(),
                    scope.ServiceProvider.GetRequiredService<IKafkaProducer>(),
                    stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // The database may be down or the migration not applied yet: try again later.
                logger.LogWarning(ex, "Outbox relay pass failed; will retry");
                ok = false;
            }

            failedBatches = ok ? 0 : failedBatches + 1;
            var delay = ok
                ? Interval
                : TimeSpan.FromSeconds(Math.Min(Interval.TotalSeconds * Math.Pow(2, Math.Min(failedBatches - 1, 6)), MaxBackoff.TotalSeconds));

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
