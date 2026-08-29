using System.Globalization;
using MccIntakeService.Application.Abstractions;
using MccIntakeService.Domain.Common;
using MccIntakeService.Domain.Dispatch;
using MccIntakeService.Domain.Tanks;
using MccIntakeService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Wonrich.QualityPanel;

namespace MccIntakeService.Application.Dispatch;

/// <summary>A tank the bowser drew from, and how much.</summary>
public sealed record DispatchDrawCommand(string TankCode, decimal QuantityLitres);

/// <summary>The dispatch note a manager submits (SCRUM-8).</summary>
public sealed record RecordDispatchCommand(
    string BowserRegistration,
    string DriverName,
    IReadOnlyCollection<DispatchDrawCommand> Draws,
    decimal FatPercent,
    decimal Snf,
    KqColour KqColour,
    StabilityGrade StabilityGrade,
    decimal TemperatureCelsius,
    string? Remarks = null,
    DateTime? DispatchedAtLocal = null,
    string? DispatchedBy = null);

/// <summary>One source tank on a note, with the consignments it contributed.</summary>
public sealed record DispatchSourceView(
    string TankCode,
    string TankName,
    decimal QuantityLitres,
    IReadOnlyList<string> ContributingConsignments);

/// <summary>A dispatch note as read back.</summary>
public sealed record DispatchNoteView(
    string Reference,
    string BowserRegistration,
    string DriverName,
    DateTime DispatchedAtLocal,
    decimal TotalQuantityLitres,
    decimal FatPercent,
    decimal Snf,
    string KqColour,
    string StabilityGrade,
    decimal TemperatureCelsius,
    string? Remarks,
    string? DispatchedBy,
    DateTime RecordedAtUtc,
    IReadOnlyList<DispatchSourceView> Sources);

/// <summary>Bowser dispatch notes (SCRUM-8).</summary>
public interface IDispatchService
{
    /// <summary>Records a dispatch note and closes the tanks it drew from.</summary>
    Task<DispatchNoteView> RecordAsync(
        RecordDispatchCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>Reads a note back by its reference, resolved to contributing consignments.</summary>
    Task<DispatchNoteView?> GetAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>Lists notes, optionally for one dispatch date.</summary>
    Task<IReadOnlyList<DispatchNoteView>> ListAsync(
        DateOnly? dispatchDate = null,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IDispatchService" />
public sealed class DispatchService : IDispatchService
{
    private const string ReferencePrefix = "DN";

    private readonly MccIntakeDbContext _dbContext;
    private readonly IIntakeClock _clock;

    public DispatchService(MccIntakeDbContext dbContext, IIntakeClock clock)
    {
        _dbContext = dbContext;
        _clock = clock;
    }

    public async Task<DispatchNoteView> RecordAsync(
        RecordDispatchCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.Draws is null || command.Draws.Count == 0)
        {
            throw new DomainValidationException("A dispatch note must draw from at least one tank.");
        }

        var codes = command.Draws
            .Select(draw => draw.TankCode?.Trim().ToUpperInvariant() ?? string.Empty)
            .ToList();

        var tanks = await _dbContext.ChillingTanks
            .Where(tank => codes.Contains(tank.Code))
            .ToListAsync(cancellationToken);

        var missing = codes.Where(code => tanks.All(tank => tank.Code != code)).Distinct().ToArray();

        if (missing.Length > 0)
        {
            throw new EntityNotFoundException("ChillingTank", string.Join(", ", missing));
        }

        var available = new Dictionary<Guid, (ChillingTank Tank, decimal AvailableLitres)>();

        foreach (var tank in tanks)
        {
            available[tank.Id] = (tank, await AvailableLitresAsync(tank.Id, cancellationToken));
        }

        var dispatchedAtLocal = command.DispatchedAtLocal ?? _clock.LocalNow;
        var reference = await NextReferenceAsync(DateOnly.FromDateTime(dispatchedAtLocal), cancellationToken);

        var draws = command.Draws
            .Select(draw => new DispatchDraw(
                tanks.Single(tank => tank.Code == draw.TankCode.Trim().ToUpperInvariant()).Id,
                draw.QuantityLitres))
            .ToList();

        var note = DispatchNote.Record(
            Guid.NewGuid(),
            reference,
            command.BowserRegistration,
            command.DriverName,
            dispatchedAtLocal,
            new DispatchPanel(
                command.FatPercent,
                command.Snf,
                command.KqColour,
                command.StabilityGrade,
                command.TemperatureCelsius,
                command.Remarks),
            draws,
            available,
            command.DispatchedBy,
            _clock.UtcNow);

        _dbContext.DispatchNotes.Add(note);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return (await GetAsync(reference, cancellationToken))!;
    }

    public async Task<DispatchNoteView?> GetAsync(
        string reference,
        CancellationToken cancellationToken = default)
    {
        var note = await Query().FirstOrDefaultAsync(
            candidate => candidate.Reference == reference,
            cancellationToken);

        return note is null ? null : await ToViewAsync(note, cancellationToken);
    }

    public async Task<IReadOnlyList<DispatchNoteView>> ListAsync(
        DateOnly? dispatchDate = null,
        CancellationToken cancellationToken = default)
    {
        var notes = Query();

        if (dispatchDate is { } date)
        {
            notes = notes.Where(note => note.DispatchDate == date);
        }

        var results = await notes
            .OrderByDescending(note => note.DispatchedAtLocal)
            .ToListAsync(cancellationToken);

        var views = new List<DispatchNoteView>(results.Count);

        foreach (var note in results)
        {
            views.Add(await ToViewAsync(note, cancellationToken));
        }

        return views;
    }

    private IQueryable<DispatchNote> Query() =>
        _dbContext.DispatchNotes
            .AsNoTracking()
            .Include(note => note.Sources)
                .ThenInclude(source => source.Tank);

    /// <summary>
    /// What a tank currently holds: everything poured in, less anything already dispatched out.
    /// </summary>
    private async Task<decimal> AvailableLitresAsync(Guid tankId, CancellationToken cancellationToken)
    {
        var poured = await _dbContext.TankPours
            .AsNoTracking()
            .Where(pour => pour.TankId == tankId)
            .SumAsync(pour => (decimal?)pour.QuantityLitres, cancellationToken) ?? 0m;

        var dispatched = await _dbContext.DispatchSources
            .AsNoTracking()
            .Where(source => source.TankId == tankId)
            .SumAsync(source => (decimal?)source.QuantityLitres, cancellationToken) ?? 0m;

        return poured - dispatched;
    }

    /// <summary>
    /// Issues the next DN-YYYYMMDD-NN for the day. Reads the highest sequence already issued
    /// rather than counting rows, so a failed submission never hands the same one out twice.
    /// </summary>
    private async Task<string> NextReferenceAsync(DateOnly dispatchDate, CancellationToken cancellationToken)
    {
        var prefix = $"{ReferencePrefix}-{dispatchDate:yyyyMMdd}-";

        var issued = await _dbContext.DispatchNotes
            .AsNoTracking()
            .Where(note => note.DispatchDate == dispatchDate)
            .Select(note => note.Reference)
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

    private async Task<DispatchNoteView> ToViewAsync(DispatchNote note, CancellationToken cancellationToken)
    {
        var sources = new List<DispatchSourceView>();

        foreach (var source in note.Sources)
        {
            // The note resolves to its contributing consignments through the tank manifests,
            // which is what lets the factory trace a failure back to a society.
            var consignments = await _dbContext.TankPours
                .AsNoTracking()
                .Where(pour => pour.TankId == source.TankId)
                .Include(pour => pour.Consignment)
                .OrderBy(pour => pour.PouredAtUtc)
                .Select(pour => pour.Consignment!.Reference)
                .ToListAsync(cancellationToken);

            sources.Add(new DispatchSourceView(
                source.Tank?.Code ?? string.Empty,
                source.Tank?.Name ?? string.Empty,
                source.QuantityLitres,
                consignments));
        }

        return new DispatchNoteView(
            note.Reference,
            note.BowserRegistration,
            note.DriverName,
            note.DispatchedAtLocal,
            note.TotalQuantityLitres,
            note.FatPercent,
            note.Snf,
            note.KqColour,
            note.StabilityGrade,
            note.TemperatureCelsius,
            note.Remarks,
            note.DispatchedBy,
            note.RecordedAtUtc,
            sources);
    }
}
