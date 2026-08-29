using System.Globalization;
using MccIntakeService.Application.Abstractions;
using MccIntakeService.Domain.Common;
using MccIntakeService.Domain.Factory;
using MccIntakeService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MccIntakeService.Application.Factory;

/// <summary>The screening a factory intake officer submits for an arriving bowser (SCRUM-9).</summary>
public sealed record ScreenArrivalCommand(
    string DispatchNoteReference,
    bool SmellPassed,
    bool ColourPassed,
    bool TemperaturePassed,
    decimal TemperatureCelsius,
    DateTime? ArrivedAtLocal = null,
    string? ScreenedBy = null);

/// <summary>The outcome of a screening, with the batch when one was created.</summary>
public sealed record ArrivalScreeningView(
    string DispatchNoteReference,
    DateTime ArrivedAtLocal,
    bool SmellPassed,
    bool ColourPassed,
    bool TemperaturePassed,
    decimal TemperatureCelsius,
    ScreeningOutcome Outcome,
    string? FailedParameters,
    string? ScreenedBy,
    DateTime ScreenedAtUtc,
    BatchView? Batch);

/// <summary>A production batch as read back.</summary>
public sealed record BatchView(
    string Reference,
    string DispatchNoteReference,
    DateOnly BatchDate,
    DateTime ArrivedAtLocal,
    decimal ArrivalTemperatureCelsius,
    string? ScreenedBy,
    DateTime CreatedAtUtc);

/// <summary>Factory arrival screening and batch creation (SCRUM-9).</summary>
public interface IFactoryIntakeService
{
    /// <summary>Screens an arriving bowser, creating a batch only when every check passes.</summary>
    Task<ArrivalScreeningView> ScreenAsync(
        ScreenArrivalCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>Fetches a batch by its reference.</summary>
    Task<BatchView?> GetBatchAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>Lists batches, optionally by date or by originating dispatch note.</summary>
    Task<IReadOnlyList<BatchView>> ListBatchesAsync(
        DateOnly? batchDate = null,
        string? dispatchNoteReference = null,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IFactoryIntakeService" />
public sealed class FactoryIntakeService : IFactoryIntakeService
{
    private const string BatchPrefix = "WR";

    private readonly MccIntakeDbContext _dbContext;
    private readonly IIntakeClock _clock;

    public FactoryIntakeService(MccIntakeDbContext dbContext, IIntakeClock clock)
    {
        _dbContext = dbContext;
        _clock = clock;
    }

    public async Task<ArrivalScreeningView> ScreenAsync(
        ScreenArrivalCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var note = await _dbContext.DispatchNotes
            .FirstOrDefaultAsync(
                candidate => candidate.Reference == command.DispatchNoteReference,
                cancellationToken)
            ?? throw new EntityNotFoundException("DispatchNote", command.DispatchNoteReference);

        // A note is screened once, pass or fail. Screening it again would leave two answers about
        // the same bowser, and any batch it produced would stop resolving to a single arrival.
        if (await _dbContext.ArrivalScreenings.AnyAsync(
                screening => screening.DispatchNoteId == note.Id, cancellationToken))
        {
            throw new DomainValidationException(
                $"Dispatch note {command.DispatchNoteReference} has already been screened.");
        }

        var arrivedAtLocal = command.ArrivedAtLocal ?? _clock.LocalNow;

        var checks = new ScreeningChecks(
            command.SmellPassed,
            command.ColourPassed,
            command.TemperaturePassed,
            command.TemperatureCelsius);

        // The reference is only spent when the screening passes, so a rejected arrival does not
        // burn a batch number and leave a gap in the day's sequence.
        var batchReference = checks.AllPassed
            ? await NextBatchReferenceAsync(DateOnly.FromDateTime(arrivedAtLocal), cancellationToken)
            : string.Empty;

        var screening = ArrivalScreening.Screen(
            Guid.NewGuid(),
            note,
            arrivedAtLocal,
            checks,
            batchReference,
            command.ScreenedBy,
            _clock.UtcNow);

        _dbContext.ArrivalScreenings.Add(screening);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return ToView(screening, note.Reference);
    }

    public async Task<BatchView?> GetBatchAsync(
        string reference,
        CancellationToken cancellationToken = default)
    {
        var batch = await BatchQuery()
            .FirstOrDefaultAsync(candidate => candidate.Batch!.Reference == reference, cancellationToken);

        return batch is null ? null : ToBatchView(batch);
    }

    public async Task<IReadOnlyList<BatchView>> ListBatchesAsync(
        DateOnly? batchDate = null,
        string? dispatchNoteReference = null,
        CancellationToken cancellationToken = default)
    {
        var screenings = BatchQuery();

        if (batchDate is { } date)
        {
            screenings = screenings.Where(screening => screening.Batch!.BatchDate == date);
        }

        if (!string.IsNullOrWhiteSpace(dispatchNoteReference))
        {
            screenings = screenings.Where(
                screening => screening.DispatchNote!.Reference == dispatchNoteReference);
        }

        var results = await screenings
            .OrderByDescending(screening => screening.Batch!.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return results.Select(ToBatchView).ToList();
    }

    /// <summary>Screenings that produced a batch. A rejection has none, so it never appears here.</summary>
    private IQueryable<ArrivalScreening> BatchQuery() =>
        _dbContext.ArrivalScreenings
            .AsNoTracking()
            .Include(screening => screening.Batch)
            .Include(screening => screening.DispatchNote)
            .Where(screening => screening.Batch != null);

    /// <summary>
    /// Issues the next WR-YYYYMMDD-NN. Reads the highest already issued rather than counting
    /// rows, so a failed save never hands the same reference out twice.
    /// </summary>
    private async Task<string> NextBatchReferenceAsync(DateOnly batchDate, CancellationToken cancellationToken)
    {
        var prefix = $"{BatchPrefix}-{batchDate:yyyyMMdd}-";

        var issued = await _dbContext.Batches
            .AsNoTracking()
            .Where(batch => batch.BatchDate == batchDate)
            .Select(batch => batch.Reference)
            .ToListAsync(cancellationToken);

        var highest = issued
            .Select(reference => reference.StartsWith(prefix, StringComparison.Ordinal)
                && int.TryParse(reference[prefix.Length..], out var sequence)
                    ? sequence
                    : 0)
            .DefaultIfEmpty(0)
            .Max();

        return prefix + (highest + 1).ToString("D2", CultureInfo.InvariantCulture);
    }

    private static ArrivalScreeningView ToView(ArrivalScreening screening, string dispatchNoteReference) => new(
        dispatchNoteReference,
        screening.ArrivedAtLocal,
        screening.SmellPassed,
        screening.ColourPassed,
        screening.TemperaturePassed,
        screening.TemperatureCelsius,
        screening.Outcome,
        screening.FailedParameters,
        screening.ScreenedBy,
        screening.ScreenedAtUtc,
        screening.Batch is null
            ? null
            : new BatchView(
                screening.Batch.Reference,
                dispatchNoteReference,
                screening.Batch.BatchDate,
                screening.ArrivedAtLocal,
                screening.TemperatureCelsius,
                screening.ScreenedBy,
                screening.Batch.CreatedAtUtc));

    private static BatchView ToBatchView(ArrivalScreening screening) => new(
        screening.Batch!.Reference,
        screening.DispatchNote?.Reference ?? string.Empty,
        screening.Batch.BatchDate,
        screening.ArrivedAtLocal,
        screening.TemperatureCelsius,
        screening.ScreenedBy,
        screening.Batch.CreatedAtUtc);
}
