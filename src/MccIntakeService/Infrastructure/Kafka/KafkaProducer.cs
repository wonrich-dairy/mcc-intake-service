using System.Text;
using System.Text.Json;
using Confluent.Kafka;

namespace MccIntakeService.Infrastructure.Kafka;

public interface IKafkaProducer
{
    /// <summary>True only when the broker acknowledged the message. Never throws: false means "retry later".</summary>
    Task<bool> PublishAsync(string topic, string key, string payloadJson, string headersJson, string correlationId, CancellationToken cancellationToken);
}

/// <summary>
/// Confluent.Kafka producer with the same settings as Processing and Quality Lab: acks=all,
/// idempotent. Only ever called from the outbox relay after the database commit, so broker
/// trouble cannot fail an officer's submission. Configured from Kafka__* settings (SCRUM-88).
/// </summary>
public sealed class KafkaProducer : IKafkaProducer, IDisposable
{
    private readonly IProducer<string, string> _producer;
    private readonly ILogger<KafkaProducer> _logger;

    public KafkaProducer(IConfiguration config, ILogger<KafkaProducer> logger)
    {
        _logger = logger;

        var producerConfig = new ProducerConfig
        {
            BootstrapServers = config["Kafka:BootstrapServers"],
            ClientId = "mcc-intake-service",
            Acks = Acks.All,
            EnableIdempotence = true,
            MessageTimeoutMs = 10000,
        };

        var securityProtocol = config["Kafka:SecurityProtocol"];
        if (!string.IsNullOrWhiteSpace(securityProtocol) &&
            Enum.TryParse<SecurityProtocol>(securityProtocol, true, out var protocol) &&
            protocol != SecurityProtocol.Plaintext)
        {
            producerConfig.SecurityProtocol = protocol;
            producerConfig.SaslMechanism = Enum.TryParse<SaslMechanism>(config["Kafka:SaslMechanism"], true, out var mechanism)
                ? mechanism
                : SaslMechanism.Plain;
            producerConfig.SaslUsername = config["Kafka:SaslUsername"];
            producerConfig.SaslPassword = config["Kafka:SaslPassword"];
        }

        _producer = new ProducerBuilder<string, string>(producerConfig)
            .SetErrorHandler((_, e) => _logger.LogError("Kafka producer error: {Reason}", e.Reason))
            .Build();
    }

    public async Task<bool> PublishAsync(string topic, string key, string payloadJson, string headersJson, string correlationId, CancellationToken cancellationToken)
    {
        try
        {
            var headers = new Headers { { "x-correlation-id", Encoding.UTF8.GetBytes(correlationId) } };

            try
            {
                var extra = JsonSerializer.Deserialize<Dictionary<string, string>>(headersJson);
                foreach (var (name, value) in extra ?? [])
                {
                    if (name is "x-correlation-id") continue;
                    headers.Add(name, Encoding.UTF8.GetBytes(value ?? string.Empty));
                }
            }
            catch (JsonException)
            {
                // Malformed headers must not block the event itself.
            }

            var result = await _producer.ProduceAsync(topic, new Message<string, string>
            {
                Key = key,
                Value = payloadJson,
                Headers = headers,
            }, cancellationToken);

            _logger.LogInformation("Published to {Topic} key={Key} correlationId={CorrelationId} partition={Partition} offset={Offset}",
                topic, key, correlationId, result.Partition.Value, result.Offset.Value);

            return result.Status == PersistenceStatus.Persisted;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to publish to {Topic} key={Key} correlationId={CorrelationId}; will retry",
                topic, key, correlationId);
            return false;
        }
    }

    public void Dispose()
    {
        _producer.Flush(TimeSpan.FromSeconds(5));
        _producer.Dispose();
    }
}

/// <summary>
/// Used when Kafka:BootstrapServers is not configured. Returns false, so outbox rows stay Pending:
/// events are delayed, not lost.
/// </summary>
public sealed class NoOpKafkaProducer(ILogger<NoOpKafkaProducer> logger) : IKafkaProducer
{
    public Task<bool> PublishAsync(string topic, string key, string payloadJson, string headersJson, string correlationId, CancellationToken cancellationToken)
    {
        logger.LogWarning("Kafka is not configured: the event for {Topic} key={Key} stays Pending in outbox_messages", topic, key);
        return Task.FromResult(false);
    }
}
