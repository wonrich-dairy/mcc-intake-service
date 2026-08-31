using MccIntakeService.Application.Consignments;
using MccIntakeService.Application.QualityTests;
using MccIntakeService.Application.Sync;
using MccIntakeService.Application.Tanks;
using MccIntakeService.Configuration;
using MccIntakeService.Domain.QualityTests;
using MccIntakeService.Domain.Sync;
using MccIntakeService.Infrastructure.Persistence;
using MccIntakeService.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Wonrich.QualityPanel;
using Wonrich.QualityPanel.Configuration;

namespace MccIntakeService.Tests.Application;

/// <summary>Covers uploading an officer's offline queue (SCRUM-10).</summary>
public class SyncServiceTests : IDisposable
{
    private readonly TestDatabase _database = TestDatabase.Create();

    private readonly FakeIntakeClock _clock = new(new DateTime(2026, 8, 23, 8, 0, 0));

    private SyncService CreateService(out MccIntakeDbContext context)
    {
        context = _database.CreateContext();

        return new SyncService(
            context,
            new ConsignmentService(
                context,
                new ConsignmentReferenceGenerator(context),
                _clock,
                Options.Create(new IntakeOptions()),
                NullLogger<ConsignmentService>.Instance),
            new QualityTestService(
                context,
                new QualityPanelEvaluator(Options.Create(new QualityThresholds())),
                _clock),
            new TankService(context, _clock));
    }

    private SyncOperation Registration(
        string clientId,
        int sequence,
        string societyCode = "KC",
        DateTime? capturedAt = null) =>
        new(clientId, sequence, SyncOperationKind.RegisterConsignment,
            new SyncConsignmentPayload(
                _database.Society(societyCode).Id,
                [new CanEntryPayload(1, 41.2m)],
                capturedAt ?? _clock.LocalNow));

    private static SyncOperation Panel(string clientId, int sequence, string consignmentReference) =>
        new(clientId, sequence, SyncOperationKind.RecordQualityTest, null,
            new SyncQualityTestPayload(
                consignmentReference, 4.1m, 28.5m, 29.0m, 0m, KqColour.Blue,
                new Dictionary<AlcoholStage, StageOutcome> { [AlcoholStage.Alcohol80] = StageOutcome.Negative },
                TestVerdict.Accept, null, null));

    private static SyncOperation Pour(string clientId, int sequence, string consignmentReference) =>
        new(clientId, sequence, SyncOperationKind.PourToTank, null, null,
            new SyncPourPayload("T1", consignmentReference));

    [Fact]
    public async Task A_record_captured_before_the_cutoff_applies_however_late_it_is_uploaded()
    {
        // The morning's intake, uploaded once the officer found coverage in the evening. Judging
        // it by the moment of upload would reject it against the 16:00 cutoff - intake blocked by
        // network conditions, which is the outcome the story rules out.
        var capturedAt = new DateTime(2026, 8, 23, 9, 0, 0);
        _clock.LocalNow = new DateTime(2026, 8, 23, 18, 0, 0);

        var service = CreateService(out var context);
        await using var _ = context;

        var result = await service.UploadAsync(
            [Registration("client-1", 1, capturedAt: capturedAt)], "officer-1");

        var applied = Assert.Single(result.Results);

        Assert.Equal(SyncStatus.Applied, applied.Status);

        await using var check = _database.CreateContext();
        var consignment = await check.Consignments.SingleAsync();

        Assert.Equal(capturedAt, consignment.ArrivalAtLocal);
    }

    [Fact]
    public async Task A_record_that_does_not_say_when_it_was_taken_fails_rather_than_being_dated_on_upload()
    {
        var service = CreateService(out var context);
        await using var _ = context;

        var operation = new SyncOperation(
            "client-1", 1, SyncOperationKind.RegisterConsignment,
            new SyncConsignmentPayload(_database.Society("KC").Id, [new CanEntryPayload(1, 41.2m)], null));

        var result = await service.UploadAsync([operation], "officer-1");

        var failed = Assert.Single(result.Results);

        Assert.Equal(SyncStatus.Failed, failed.Status);
        Assert.Contains("arrival time", failed.Error!, StringComparison.OrdinalIgnoreCase);

        // And nothing was written for it: a record the server cannot date is not half-applied.
        await using var check = _database.CreateContext();
        Assert.Equal(0, await check.Consignments.CountAsync());
        Assert.Equal(0, await check.SyncedRecords.CountAsync());
    }

    [Fact]
    public async Task A_failed_record_leaves_nothing_behind_for_the_next_one_to_commit()
    {
        var service = CreateService(out var context);
        await using var _ = context;

        var bad = new SyncOperation(
            "client-1", 1, SyncOperationKind.RegisterConsignment,
            new SyncConsignmentPayload(_database.Society("KC").Id, [new CanEntryPayload(1, 41.2m)], null));

        var result = await service.UploadAsync([bad, Registration("client-2", 2)], "officer-1");

        Assert.Equal(1, result.Failed);
        Assert.Equal(1, result.Applied);

        // Exactly one consignment: the failed operation's work must not ride into the database on
        // the back of the one that followed it.
        await using var check = _database.CreateContext();
        Assert.Equal(1, await check.Consignments.CountAsync());
        Assert.Equal(1, await check.SyncedRecords.CountAsync());
    }

    [Fact]
    public async Task A_queued_registration_is_applied_and_given_a_server_reference()
    {
        var service = CreateService(out var context);
        await using var _ = context;

        var result = await service.UploadAsync([Registration("client-1", 1)], "officer-1");

        var applied = Assert.Single(result.Results);
        Assert.Equal(SyncStatus.Applied, applied.Status);
        Assert.StartsWith("MCC-", applied.Reference);
        Assert.Equal(1, result.Applied);
    }

    [Fact]
    public async Task Replaying_the_same_queue_applies_nothing_twice()
    {
        var service = CreateService(out var context);
        await using var _ = context;

        var first = await service.UploadAsync([Registration("client-1", 1)], "officer-1");
        var replay = await service.UploadAsync([Registration("client-1", 1)], "officer-1");

        // A handheld that drops mid-upload can only resend; the server has to be the side that
        // remembers, and it hands back the reference the first attempt produced.
        Assert.Equal(SyncStatus.Duplicate, Assert.Single(replay.Results).Status);
        Assert.Equal(first.Results[0].Reference, replay.Results[0].Reference);
        Assert.Equal(1, replay.Duplicates);

        await using var verification = _database.CreateContext();
        Assert.Single(verification.Consignments);
    }

    [Fact]
    public async Task Records_apply_in_creation_order_regardless_of_the_order_sent()
    {
        var service = CreateService(out var context);
        await using var _ = context;

        var registration = await service.UploadAsync([Registration("client-1", 1)], "officer-1");
        var reference = registration.Results[0].Reference!;

        // The pour depends on the panel that accepted the consignment, so sequence must govern.
        var result = await service.UploadAsync(
            [Pour("client-3", 3, reference), Panel("client-2", 2, reference)],
            "officer-1");

        Assert.All(result.Results, r => Assert.Equal(SyncStatus.Applied, r.Status));
        Assert.Equal(["client-2", "client-3"], result.Results.Select(r => r.ClientRecordId));
    }

    [Fact]
    public async Task A_whole_shift_uploads_as_one_queue()
    {
        var service = CreateService(out var context);
        await using var _ = context;

        var registration = await service.UploadAsync([Registration("client-1", 1)], "officer-1");
        var reference = registration.Results[0].Reference!;

        var result = await service.UploadAsync(
            [Panel("client-2", 2, reference), Pour("client-3", 3, reference)],
            "officer-1");

        Assert.Equal(2, result.Applied);
        Assert.Equal(0, result.Failed);

        await using var verification = _database.CreateContext();
        Assert.Single(verification.QualityTests);
        Assert.Single(verification.TankPours);
    }

    [Fact]
    public async Task A_failing_record_does_not_sink_the_queue_behind_it()
    {
        var service = CreateService(out var context);
        await using var _ = context;

        var result = await service.UploadAsync(
            [
                Panel("client-1", 1, "MCC-20260823-XX-99"),
                Registration("client-2", 2)
            ],
            "officer-1");

        Assert.Equal(SyncStatus.Failed, result.Results[0].Status);
        Assert.Equal(SyncStatus.Applied, result.Results[1].Status);
        Assert.Equal(1, result.Failed);
        Assert.Equal(1, result.Applied);
    }

    [Fact]
    public async Task A_failed_record_is_reported_with_a_reason_and_never_recorded_as_synced()
    {
        var service = CreateService(out var context);
        await using var _ = context;

        var result = await service.UploadAsync([Panel("client-1", 1, "MCC-20260823-XX-99")], "officer-1");

        Assert.False(string.IsNullOrWhiteSpace(result.Results[0].Error));

        // Nothing is remembered, so the client can retry it once the cause is fixed.
        await using var verification = _database.CreateContext();
        Assert.Empty(verification.SyncedRecords);
    }

    [Fact]
    public async Task A_retry_after_fixing_the_cause_succeeds()
    {
        var service = CreateService(out var context);
        await using var _ = context;

        var failed = await service.UploadAsync([Panel("client-2", 2, "MCC-20260823-XX-99")], "officer-1");
        Assert.Equal(SyncStatus.Failed, failed.Results[0].Status);

        var registration = await service.UploadAsync([Registration("client-1", 1)], "officer-1");
        var retry = await service.UploadAsync(
            [Panel("client-2", 2, registration.Results[0].Reference!)],
            "officer-1");

        Assert.Equal(SyncStatus.Applied, retry.Results[0].Status);
    }

    [Fact]
    public async Task References_are_issued_by_the_server_so_offline_records_cannot_collide()
    {
        var service = CreateService(out var context);
        await using var _ = context;

        // Two devices queue a consignment for the same society on the same day, neither aware of
        // the other. The server hands out distinct references.
        var result = await service.UploadAsync(
            [Registration("device-a", 1), Registration("device-b", 2)],
            "officer-1");

        var references = result.Results.Select(r => r.Reference).ToList();

        Assert.Equal(2, references.Distinct().Count());
        Assert.All(references, reference => Assert.StartsWith("MCC-20260823-KC-", reference));
    }

    [Fact]
    public async Task A_record_with_no_client_identifier_is_refused_rather_than_applied()
    {
        var service = CreateService(out var context);
        await using var _ = context;

        var result = await service.UploadAsync(
            [Registration("   ", 1)],
            "officer-1");

        // Without an identifier the upload cannot be made idempotent, so it is not attempted.
        Assert.Equal(SyncStatus.Failed, result.Results[0].Status);

        await using var verification = _database.CreateContext();
        Assert.Empty(verification.Consignments);
    }

    [Fact]
    public async Task A_record_missing_its_payload_fails_without_taking_the_queue_with_it()
    {
        var service = CreateService(out var context);
        await using var _ = context;

        var result = await service.UploadAsync(
            [
                new SyncOperation("client-1", 1, SyncOperationKind.RegisterConsignment),
                Registration("client-2", 2)
            ],
            "officer-1");

        Assert.Equal(SyncStatus.Failed, result.Results[0].Status);
        Assert.Equal(SyncStatus.Applied, result.Results[1].Status);
    }

    [Fact]
    public async Task An_empty_queue_uploads_cleanly()
    {
        var service = CreateService(out var context);
        await using var _ = context;

        var result = await service.UploadAsync([], "officer-1");

        Assert.Empty(result.Results);
        Assert.Equal(0, result.Applied);
    }

    [Fact]
    public async Task A_synced_record_is_indistinguishable_from_one_entered_online()
    {
        var service = CreateService(out var context);
        await using var _ = context;

        var result = await service.UploadAsync([Registration("client-1", 1)], "officer-1");

        await using var verification = _database.CreateContext();
        var consignment = await verification.Consignments
            .SingleAsync(c => c.Reference == result.Results[0].Reference);

        // Nothing on the consignment marks it as having arrived through the sync queue.
        Assert.Equal("officer-1", consignment.RegisteredBy);
        Assert.Equal(MccIntakeService.Domain.Consignments.ConsignmentStatus.Registered, consignment.Status);
    }

    [Fact]
    public async Task The_officer_who_uploaded_is_recorded_against_the_sync()
    {
        var service = CreateService(out var context);
        await using var _ = context;

        await service.UploadAsync([Registration("client-1", 1)], "officer-7");

        await using var verification = _database.CreateContext();
        var record = await verification.SyncedRecords.SingleAsync();

        Assert.Equal("officer-7", record.SyncedBy);
        Assert.Equal(SyncOperationKind.RegisterConsignment, record.Kind);
    }

    [Fact]
    public async Task A_null_queue_is_rejected()
    {
        var service = CreateService(out var context);
        await using var _ = context;

        await Assert.ThrowsAsync<ArgumentNullException>(() => service.UploadAsync(null!, "officer-1"));
    }

    public void Dispose()
    {
        _database.Dispose();
        GC.SuppressFinalize(this);
    }
}
