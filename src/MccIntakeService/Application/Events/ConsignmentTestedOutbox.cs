using System.Text.Json;
using MccIntakeService.Api.Infrastructure;
using MccIntakeService.Application.Traceability;
using MccIntakeService.Domain.Consignments;
using MccIntakeService.Domain.Outbox;
using MccIntakeService.Domain.QualityTests;
using MccIntakeService.Infrastructure.Persistence;
using Microsoft.Extensions.Options;
using Wonrich.MccIntake.Contracts.Events;
using Wonrich.QualityPanel.Configuration;

namespace MccIntakeService.Application.Events;

/// <summary>Settings for the ConsignmentTested event, bound from the "ConsignmentTested" section.</summary>
public sealed class ConsignmentTestedOptions
{
    public const string SectionName = "ConsignmentTested";

    /// <summary>Topic the event is published on.</summary>
    public string Topic { get; set; } = MccIntakeTopics.ConsignmentTested;

    /// <summary>
    /// An accepted consignment is marginal when its tightest measure (MarginToThreshold) sat
    /// within this fraction of its scale from the limit. 0.10 means within 10%.
    /// </summary>
    public decimal MarginalMargin { get; set; } = 0.10m;
}

/// <summary>Stages a ConsignmentTested event for a gate verdict (SCRUM-141).</summary>
public interface IConsignmentTestedOutbox
{
    /// <summary>
    /// Adds the event to outbox_messages. It does not save and does not touch Kafka: the caller
    /// saves it in the same SaveChanges as the verdict, so a rollback publishes nothing.
    /// </summary>
    /// <param name="test">The recorded test.</param>
    /// <param name="consignment">Its consignment, with the society and cans loaded.</param>
    void Stage(QualityTest test, Consignment consignment);
}

/// <inheritdoc cref="IConsignmentTestedOutbox" />
public sealed class ConsignmentTestedOutbox(
    MccIntakeDbContext db,
    IOptions<QualityThresholds> thresholds,
    IOptions<ConsignmentTestedOptions> options,
    TimeProvider time,
    IHttpContextAccessor? http = null) : IConsignmentTestedOutbox
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public void Stage(QualityTest test, Consignment consignment)
    {
        ArgumentNullException.ThrowIfNull(test);
        ArgumentNullException.ThrowIfNull(consignment);

        var society = consignment.Society
            ?? throw new InvalidOperationException($"Consignment '{consignment.Reference}' was loaded without its society.");

        var correlationId = http?.HttpContext?.Items[CorrelationIdMiddleware.HeaderName] as string
            ?? Guid.NewGuid().ToString();

        var accepted = test.Verdict == TestVerdict.Accept;
        var tested = new ConsignmentTestedEvent
        {
            CorrelationId = correlationId,
            ConsignmentReference = consignment.Reference,
            Society = new SocietyDetails { Code = society.Code, Name = society.Name },
            Cans = consignment.Cans
                .OrderBy(can => can.CanNumber)
                .Select(can => new CanDetails { Label = can.CanLabel, QuantityLitres = can.QuantityLitres })
                .ToList(),
            QuantityLitres = consignment.TotalQuantityLitres,
            Panel = new QualityPanelDetails
            {
                FatPercent = test.FatPercent,
                RawLactometerReading = test.RawLactometerReading,
                TemperatureCelsius = test.TemperatureCelsius,
                WaterPercent = test.WaterPercent,
                KqColour = test.KqColour,
                SmellOk = test.SmellOk,
                ColourOk = test.ColourOk,
                TasteOk = test.TasteOk,
                CorrectedClr = test.CorrectedClr,
                Snf = test.Snf,
                TotalSolids = test.TotalSolids,
                StabilityGrade = test.StabilityGrade,
                PassedAlcoholAt = test.PassedAlcoholAt,
            },
            Verdict = test.Verdict.ToString(),
            FailedParameter = test.FailedParameter,
            FailedValue = test.FailedValue,
            // A rejection is not "marginal": it failed outright.
            IsMarginal = accepted && MarginToThreshold.Tightest(test, thresholds.Value) < options.Value.MarginalMargin,
            OccurredAtUtc = DateTime.SpecifyKind(test.TestedAtUtc, DateTimeKind.Utc),
        };

        var headers = new Dictionary<string, string>
        {
            ["x-correlation-id"] = correlationId,
            ["eventId"] = tested.EventId.ToString(),
            ["eventType"] = tested.EventType,
            ["schemaVersion"] = tested.SchemaVersion,
            ["timestamp"] = tested.OccurredAtUtc.ToString("o"),
        };

        db.OutboxMessages.Add(new OutboxMessage
        {
            Id = Guid.NewGuid(),
            Topic = options.Value.Topic,
            // Keyed by society, so one society's verdicts stay in order on the partition.
            Key = society.Code,
            EventType = tested.EventType,
            Payload = JsonSerializer.Serialize(tested, Json),
            HeadersJson = JsonSerializer.Serialize(headers),
            CreatedAtUtc = time.GetUtcNow().UtcDateTime,
            Status = OutboxStatus.Pending,
            CorrelationId = correlationId,
        });
    }
}
