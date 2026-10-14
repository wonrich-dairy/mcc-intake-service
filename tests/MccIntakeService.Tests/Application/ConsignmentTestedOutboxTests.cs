using System.Text.Json;
using MccIntakeService.Application.Consignments;
using MccIntakeService.Application.Events;
using MccIntakeService.Application.QualityTests;
using MccIntakeService.Application.Sync;
using MccIntakeService.Application.Tanks;
using MccIntakeService.Application.Traceability;
using MccIntakeService.Configuration;
using MccIntakeService.Domain.Common;
using MccIntakeService.Domain.Consignments;
using MccIntakeService.Domain.Outbox;
using MccIntakeService.Domain.QualityTests;
using MccIntakeService.Domain.Sync;
using MccIntakeService.Infrastructure.Kafka;
using MccIntakeService.Infrastructure.Persistence;
using MccIntakeService.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Wonrich.MccIntake.Contracts.Events;
using Wonrich.QualityPanel;
using Wonrich.QualityPanel.Configuration;

namespace MccIntakeService.Tests.Application;

/// <summary>
/// Covers the ConsignmentTested event (SCRUM-141): staged with every gate verdict in the same
/// transaction, relayed to Kafka afterwards, and never failing the officer's submission.
/// </summary>
public class ConsignmentTestedOutboxTests : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly TestDatabase _database = TestDatabase.Create();

    private readonly FakeIntakeClock _clock = new(new DateTime(2026, 8, 23, 8, 0, 0));

    private QualityTestService CreateService(MccIntakeDbContext context) =>
        new(
            context,
            new QualityPanelEvaluator(Options.Create(new QualityThresholds())),
            _clock,
            new ConsignmentTestedOutbox(
                context,
                Options.Create(new QualityThresholds()),
                Options.Create(new ConsignmentTestedOptions()),
                TimeProvider.System));

    private async Task<string> RegisterConsignmentAsync(string societyCode = "KC")
    {
        await using var context = _database.CreateContext();

        var service = new ConsignmentService(
            context,
            new ConsignmentReferenceGenerator(context),
            _clock,
            Options.Create(new IntakeOptions()),
            NullLogger<ConsignmentService>.Instance);

        var view = await service.RegisterAsync(
            new RegisterConsignmentCommand(_database.Society(societyCode).Id, [new CanEntry(1, 41.2m), new CanEntry(2, 30m)]));

        return view.Reference;
    }

    private static RecordTestCommand Panel(
        TestVerdict verdict = TestVerdict.Accept,
        decimal fat = 4.1m,
        string? failedParameter = null,
        string? failedValue = null,
        DateTime? testedAtUtc = null) =>
        new(fat, 28.5m, 29.0m, 0m, KqColour.Blue,
            new Dictionary<AlcoholStage, StageOutcome> { [AlcoholStage.Alcohol80] = StageOutcome.Negative },
            verdict,
            FailedParameter: failedParameter,
            FailedValue: failedValue,
            TestedBy: "officer-1",
            TestedAtUtc: testedAtUtc);

    private List<OutboxMessage> Outbox()
    {
        using var context = _database.CreateContext();
        return context.OutboxMessages.AsNoTracking().OrderBy(message => message.CreatedAtUtc).ToList();
    }

    private static ConsignmentTestedEvent Event(OutboxMessage message) =>
        JsonSerializer.Deserialize<ConsignmentTestedEvent>(message.Payload, Json)!;

    [Fact]
    public async Task An_accepted_verdict_stages_one_event_keyed_by_society()
    {
        var reference = await RegisterConsignmentAsync();
        await using var context = _database.CreateContext();

        await CreateService(context).RecordAsync(reference, Panel());

        var message = Assert.Single(Outbox());
        Assert.Equal(MccIntakeTopics.ConsignmentTested, message.Topic);
        Assert.Equal("KC", message.Key);
        Assert.Equal(OutboxStatus.Pending, message.Status);
        Assert.Equal("ConsignmentTested", message.EventType);

        var headers = JsonSerializer.Deserialize<Dictionary<string, string>>(message.HeadersJson)!;
        Assert.Equal("ConsignmentTested", headers["eventType"]);
        Assert.Equal("v1", headers["schemaVersion"]);
        Assert.False(string.IsNullOrEmpty(headers["x-correlation-id"]));

        var tested = Event(message);
        Assert.Equal(headers["eventId"], tested.EventId.ToString());
        Assert.Equal(reference, tested.ConsignmentReference);
        Assert.Equal("KC", tested.Society.Code);
        Assert.False(string.IsNullOrEmpty(tested.Society.Name));
        Assert.Equal(["KC 01", "KC 02"], tested.Cans.Select(can => can.Label));
        Assert.Equal(tested.Cans.Sum(can => can.QuantityLitres), tested.QuantityLitres);
        Assert.Equal("Accept", tested.Verdict);
        Assert.Null(tested.FailedParameter);
        Assert.Equal(4.1m, tested.Panel.FatPercent);
        Assert.True(tested.Panel.Snf > 0);
        Assert.Equal(_clock.UtcNow.UtcDateTime, tested.OccurredAtUtc);
    }

    [Fact]
    public async Task A_rejected_verdict_is_published_too_with_the_failed_parameter()
    {
        var reference = await RegisterConsignmentAsync();
        await using var context = _database.CreateContext();

        await CreateService(context).RecordAsync(
            reference, Panel(TestVerdict.Reject, fat: 2.9m, failedParameter: "FatPercent", failedValue: "2.90"));

        var tested = Event(Assert.Single(Outbox()));
        Assert.Equal("Reject", tested.Verdict);
        Assert.Equal("FatPercent", tested.FailedParameter);
        Assert.Equal("2.90", tested.FailedValue);
        Assert.False(tested.IsMarginal);
    }

    [Fact]
    public async Task An_acceptance_close_to_a_limit_is_marked_marginal()
    {
        var marginal = await RegisterConsignmentAsync();
        var comfortable = await RegisterConsignmentAsync("KC");
        await using var context = _database.CreateContext();
        var service = CreateService(context);

        // 3.55% fat against a 3.5% floor.
        await service.RecordAsync(marginal, Panel(fat: 3.55m));
        await service.RecordAsync(comfortable, Panel(fat: 4.1m));

        var events = Outbox().Select(Event).ToDictionary(tested => tested.ConsignmentReference);
        Assert.True(events[marginal].IsMarginal);

        // The flag is MarginToThreshold's score against the configured margin, whatever the measure.
        var test = await context.QualityTests.Include(t => t.Consignment).SingleAsync(t => t.Consignment!.Reference == comfortable);
        Assert.Equal(MarginToThreshold.Tightest(test, new QualityThresholds()) < 0.10m, events[comfortable].IsMarginal);
    }

    [Fact]
    public async Task A_rolled_back_transaction_publishes_nothing()
    {
        var reference = await RegisterConsignmentAsync();
        await using var context = _database.CreateContext();

        await using (var transaction = await context.Database.BeginTransactionAsync())
        {
            await CreateService(context).RecordAsync(reference, Panel());
            await transaction.RollbackAsync();
        }

        Assert.Empty(Outbox());
        await using var check = _database.CreateContext();
        Assert.Empty(check.QualityTests);
    }

    [Fact]
    public async Task A_refused_second_verdict_stages_no_second_event()
    {
        var reference = await RegisterConsignmentAsync();
        await using var context = _database.CreateContext();
        var service = CreateService(context);

        await service.RecordAsync(reference, Panel());
        await Assert.ThrowsAsync<ConsignmentAlreadyTestedException>(() => service.RecordAsync(reference, Panel()));

        Assert.Single(Outbox());
    }

    [Fact]
    public async Task A_broker_outage_does_not_fail_the_submission_and_the_event_is_relayed_on_recovery()
    {
        var reference = await RegisterConsignmentAsync();
        await using (var context = _database.CreateContext())
        {
            await CreateService(context).RecordAsync(reference, Panel());
        }

        var broker = new FakeProducer { Up = false };
        var relay = new OutboxRelay(new OutboxOptions(), TimeProvider.System, NullLogger<OutboxRelay>.Instance);

        // Broker down: the verdict is saved and the event waits.
        await using (var context = _database.CreateContext())
        {
            Assert.False(await relay.ProcessBatchAsync(context, broker, CancellationToken.None));
        }

        var waiting = Assert.Single(Outbox());
        Assert.Equal(OutboxStatus.Pending, waiting.Status);
        Assert.Equal(1, waiting.RetryCount);
        await using (var check = _database.CreateContext())
        {
            Assert.Single(check.QualityTests);
        }

        // Broker back: the same event goes out once.
        broker.Up = true;
        await using (var context = _database.CreateContext())
        {
            Assert.True(await relay.ProcessBatchAsync(context, broker, CancellationToken.None));
        }

        Assert.Equal(OutboxStatus.Processed, Assert.Single(Outbox()).Status);
        var sent = Assert.Single(broker.Published);
        Assert.Equal((MccIntakeTopics.ConsignmentTested, "KC"), (sent.Topic, sent.Key));
    }

    [Fact]
    public async Task An_event_that_keeps_failing_is_poisoned_and_parked_on_the_dead_letter_topic()
    {
        var reference = await RegisterConsignmentAsync();
        await using (var context = _database.CreateContext())
        {
            await CreateService(context).RecordAsync(reference, Panel());
        }

        var broker = new FakeProducer { Up = false, AcceptDeadLetters = true };
        var options = new OutboxOptions { MaxRetries = 2 };
        var relay = new OutboxRelay(options, TimeProvider.System, NullLogger<OutboxRelay>.Instance);

        for (var attempt = 0; attempt < 2; attempt++)
        {
            await using var context = _database.CreateContext();
            await relay.ProcessBatchAsync(context, broker, CancellationToken.None);
        }

        Assert.Equal(OutboxStatus.Poisoned, Assert.Single(Outbox()).Status);
        var parked = Assert.Single(broker.Published);
        Assert.Equal(options.DeadLetterTopic, parked.Topic);
        var headers = JsonSerializer.Deserialize<Dictionary<string, string>>(parked.HeadersJson)!;
        Assert.Equal(MccIntakeTopics.ConsignmentTested, headers["x-original-topic"]);
    }

    [Fact]
    public async Task A_panel_synced_from_offline_keeps_the_time_it_was_taken()
    {
        var reference = await RegisterConsignmentAsync();
        var takenAt = _clock.UtcNow.UtcDateTime.AddHours(-6);
        await using var context = _database.CreateContext();

        var sync = new SyncService(
            context,
            new ConsignmentService(context, new ConsignmentReferenceGenerator(context), _clock,
                Options.Create(new IntakeOptions()), NullLogger<ConsignmentService>.Instance),
            CreateService(context),
            new TankService(context, _clock));

        var result = await sync.UploadAsync(
            [
                new SyncOperation("offline-panel-1", 1, SyncOperationKind.RecordQualityTest, null,
                    new SyncQualityTestPayload(
                        reference, 4.1m, 28.5m, 29.0m, 0m, KqColour.Blue,
                        new Dictionary<AlcoholStage, StageOutcome> { [AlcoholStage.Alcohol80] = StageOutcome.Negative },
                        TestVerdict.Accept, null, null, TestedAtUtc: takenAt))
            ],
            "officer-1");

        Assert.Equal(1, result.Applied);
        Assert.Equal(takenAt, Event(Assert.Single(Outbox())).OccurredAtUtc);
        await using var check = _database.CreateContext();
        Assert.Equal(takenAt, (await check.QualityTests.SingleAsync()).TestedAtUtc);
    }

    [Fact]
    public async Task A_test_time_in_the_future_is_refused()
    {
        var reference = await RegisterConsignmentAsync();
        await using var context = _database.CreateContext();

        await Assert.ThrowsAsync<DomainValidationException>(() =>
            CreateService(context).RecordAsync(reference, Panel(testedAtUtc: _clock.UtcNow.UtcDateTime.AddHours(1))));

        Assert.Empty(Outbox());
    }

    [Fact]
    public async Task The_payload_uses_the_camelCase_names_the_traceability_consumer_reads()
    {
        var reference = await RegisterConsignmentAsync();
        await using var context = _database.CreateContext();
        await CreateService(context).RecordAsync(reference, Panel());

        using var payload = JsonDocument.Parse(Assert.Single(Outbox()).Payload);
        var root = payload.RootElement;

        // The fields Traceability's ConsignmentTestedHandler (PR #18) reads.
        Assert.Equal("KC", root.GetProperty("society").GetProperty("code").GetString());
        Assert.Equal("Accept", root.GetProperty("verdict").GetString());
        Assert.Equal(4.1m, root.GetProperty("panel").GetProperty("fatPercent").GetDecimal());
        Assert.True(root.GetProperty("panel").TryGetProperty("snf", out _));
        Assert.True(root.TryGetProperty("isMarginal", out _));
        Assert.True(root.TryGetProperty("occurredAtUtc", out _));
        Assert.Equal("v1", root.GetProperty("schemaVersion").GetString());
        Assert.Equal(JsonValueKind.Array, root.GetProperty("cans").ValueKind);
    }

    public void Dispose() => _database.Dispose();

    private sealed class FakeProducer : IKafkaProducer
    {
        public bool Up { get; set; }

        public bool AcceptDeadLetters { get; set; }

        public List<(string Topic, string Key, string HeadersJson)> Published { get; } = [];

        public Task<bool> PublishAsync(string topic, string key, string payloadJson, string headersJson, string correlationId, CancellationToken cancellationToken)
        {
            var accepted = Up || (AcceptDeadLetters && topic.StartsWith("wonrich.dlq.", StringComparison.Ordinal));
            if (accepted)
            {
                Published.Add((topic, key, headersJson));
            }

            return Task.FromResult(accepted);
        }
    }
}
