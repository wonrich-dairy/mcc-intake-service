using MccIntakeService.Application.Consignments;
using MccIntakeService.Application.QualityTests;
using MccIntakeService.Application.Tanks;
using MccIntakeService.Domain.Common;
using MccIntakeService.Domain.QualityTests;
using MccIntakeService.Domain.Sync;
using MccIntakeService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Wonrich.QualityPanel;

namespace MccIntakeService.Application.Sync;

/// <summary>One record from an officer's offline queue.</summary>
/// <param name="ClientRecordId">The client's own identifier for the record.</param>
/// <param name="Sequence">Position in the queue, so creation order is preserved.</param>
/// <param name="Kind">What the record is.</param>
/// <param name="Consignment">Payload when registering a consignment.</param>
/// <param name="QualityTest">Payload when recording a gate panel.</param>
/// <param name="Pour">Payload when pouring into a tank.</param>
public sealed record SyncOperation(
    string ClientRecordId,
    int Sequence,
    SyncOperationKind Kind,
    SyncConsignmentPayload? Consignment = null,
    SyncQualityTestPayload? QualityTest = null,
    SyncPourPayload? Pour = null);

/// <summary>A consignment captured offline.</summary>
public sealed record SyncConsignmentPayload(
    Guid SocietyId,
    IReadOnlyCollection<CanEntryPayload> Cans,
    DateTime? ArrivalAtLocal);

/// <summary>One can on an offline consignment sheet.</summary>
public sealed record CanEntryPayload(int CanNumber, decimal QuantityKg);

/// <summary>A gate panel captured offline.</summary>
public sealed record SyncQualityTestPayload(
    string ConsignmentReference,
    decimal FatPercent,
    decimal RawLactometerReading,
    decimal TemperatureCelsius,
    decimal WaterPercent,
    KqColour KqColour,
    IReadOnlyDictionary<AlcoholStage, StageOutcome> AlcoholOutcomes,
    TestVerdict Verdict,
    string? FailedParameter,
    string? FailedValue);

/// <summary>A pour captured offline.</summary>
public sealed record SyncPourPayload(string TankCode, string ConsignmentReference);

/// <summary>How one queued record fared.</summary>
public enum SyncStatus
{
    /// <summary>The record was applied on this upload.</summary>
    Applied = 0,

    /// <summary>The record had already been applied; the original result is returned.</summary>
    Duplicate = 1,

    /// <summary>The record could not be applied. The client keeps it for manual review.</summary>
    Failed = 2
}

/// <summary>The outcome for one queued record.</summary>
/// <param name="ClientRecordId">The client's identifier, so it can match the result to its queue.</param>
/// <param name="Status">Applied, Duplicate or Failed.</param>
/// <param name="Reference">Reference the server assigned, when the record produced one.</param>
/// <param name="Error">Why it failed, when it did.</param>
public sealed record SyncResult(string ClientRecordId, SyncStatus Status, string? Reference, string? Error);

/// <summary>The outcome of one upload.</summary>
/// <param name="Results">One entry per submitted record, in the order they were sent.</param>
/// <param name="Applied">How many were applied on this upload.</param>
/// <param name="Duplicates">How many had already been applied.</param>
/// <param name="Failed">How many the client must keep for manual review.</param>
public sealed record SyncBatchResult(
    IReadOnlyList<SyncResult> Results,
    int Applied,
    int Duplicates,
    int Failed);

/// <summary>Uploads an officer's offline queue (SCRUM-10).</summary>
public interface ISyncService
{
    /// <summary>Applies a queue in creation order, reporting each record separately.</summary>
    Task<SyncBatchResult> UploadAsync(
        IReadOnlyCollection<SyncOperation> operations,
        string? syncedBy,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="ISyncService" />
public sealed class SyncService : ISyncService
{
    private readonly MccIntakeDbContext _dbContext;
    private readonly IConsignmentService _consignments;
    private readonly IQualityTestService _tests;
    private readonly ITankService _tanks;

    public SyncService(
        MccIntakeDbContext dbContext,
        IConsignmentService consignments,
        IQualityTestService tests,
        ITankService tanks)
    {
        _dbContext = dbContext;
        _consignments = consignments;
        _tests = tests;
        _tanks = tanks;
    }

    public async Task<SyncBatchResult> UploadAsync(
        IReadOnlyCollection<SyncOperation> operations,
        string? syncedBy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operations);

        var results = new List<SyncResult>();

        // Creation order matters: a panel cannot be recorded before the consignment it belongs to,
        // and a pour cannot happen before the panel that accepted it. The client's sequence is
        // what preserves that through a queue uploaded all at once.
        foreach (var operation in operations.OrderBy(operation => operation.Sequence))
        {
            results.Add(await ApplyAsync(operation, syncedBy, cancellationToken));
        }

        return new SyncBatchResult(
            results,
            results.Count(result => result.Status == SyncStatus.Applied),
            results.Count(result => result.Status == SyncStatus.Duplicate),
            results.Count(result => result.Status == SyncStatus.Failed));
    }

    private async Task<SyncResult> ApplyAsync(
        SyncOperation operation,
        string? syncedBy,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(operation.ClientRecordId))
        {
            return new SyncResult(
                operation.ClientRecordId ?? string.Empty,
                SyncStatus.Failed,
                null,
                "The record carries no client identifier, so it cannot be uploaded safely.");
        }

        var clientRecordId = operation.ClientRecordId.Trim();

        // Replay of something already taken: hand back the original answer rather than applying
        // it twice or returning a bare acknowledgement the client cannot reconcile.
        var existing = await _dbContext.SyncedRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(record => record.ClientRecordId == clientRecordId, cancellationToken);

        if (existing is not null)
        {
            return new SyncResult(clientRecordId, SyncStatus.Duplicate, existing.ResultReference, null);
        }

        try
        {
            var reference = operation.Kind switch
            {
                SyncOperationKind.RegisterConsignment =>
                    await RegisterAsync(operation, syncedBy, cancellationToken),
                SyncOperationKind.RecordQualityTest =>
                    await TestAsync(operation, syncedBy, cancellationToken),
                SyncOperationKind.PourToTank =>
                    await PourAsync(operation, syncedBy, cancellationToken),
                _ => throw new DomainValidationException($"'{operation.Kind}' is not a record this service accepts.")
            };

            _dbContext.SyncedRecords.Add(new SyncedRecord(
                Guid.NewGuid(),
                clientRecordId,
                operation.Kind,
                reference,
                syncedBy,
                DateTimeOffset.UtcNow));

            await _dbContext.SaveChangesAsync(cancellationToken);

            return new SyncResult(clientRecordId, SyncStatus.Applied, reference, null);
        }
        catch (Exception exception) when (exception is DomainException or ArgumentException)
        {
            // One bad record must not sink the queue behind it. It is reported and the client
            // keeps it for manual review; everything else in the upload still lands.
            return new SyncResult(clientRecordId, SyncStatus.Failed, null, exception.Message);
        }
    }

    private async Task<string> RegisterAsync(
        SyncOperation operation,
        string? syncedBy,
        CancellationToken cancellationToken)
    {
        var payload = operation.Consignment
            ?? throw new DomainValidationException("A consignment record carries no consignment details.");

        // The reference is issued here, by the server, exactly as it is for an online
        // registration. The client never invents one, so an offline record cannot collide with a
        // reference the server has already handed out.
        var view = await _consignments.RegisterAsync(
            new RegisterConsignmentCommand(
                payload.SocietyId,
                payload.Cans.Select(can => new Domain.Consignments.CanEntry(can.CanNumber, can.QuantityKg)).ToList(),
                payload.ArrivalAtLocal,
                syncedBy),
            cancellationToken);

        return view.Reference;
    }

    private async Task<string> TestAsync(
        SyncOperation operation,
        string? syncedBy,
        CancellationToken cancellationToken)
    {
        var payload = operation.QualityTest
            ?? throw new DomainValidationException("A quality test record carries no panel details.");

        var view = await _tests.RecordAsync(
            payload.ConsignmentReference,
            new RecordTestCommand(
                payload.FatPercent,
                payload.RawLactometerReading,
                payload.TemperatureCelsius,
                payload.WaterPercent,
                payload.KqColour,
                payload.AlcoholOutcomes,
                payload.Verdict,
                payload.FailedParameter,
                payload.FailedValue,
                syncedBy),
            cancellationToken);

        return view.ConsignmentReference;
    }

    private async Task<string> PourAsync(
        SyncOperation operation,
        string? syncedBy,
        CancellationToken cancellationToken)
    {
        var payload = operation.Pour
            ?? throw new DomainValidationException("A pour record carries no pour details.");

        var manifest = await _tanks.PourAsync(
            payload.TankCode,
            payload.ConsignmentReference,
            syncedBy,
            cancellationToken);

        return manifest.Tank.Code;
    }
}
