using MccIntakeService.Application.Abstractions;
using MccIntakeService.Domain.Common;
using MccIntakeService.Domain.Consignments;
using MccIntakeService.Domain.Tanks;
using MccIntakeService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MccIntakeService.Application.Tanks;

/// <summary>
/// A tank and the load it is holding now. The totals are the tank's current fill, not everything
/// it has ever held: a load that has gone to the factory is no longer there to be dispatched
/// again, and a list that counted it would send the manager to a 400 at submission (SCRUM-8).
/// </summary>
/// <param name="Code">Tank code as painted on the plant.</param>
/// <param name="Name">Tank name.</param>
/// <param name="CapacityLitres">Working volume of the tank.</param>
/// <param name="TotalQuantityLitres">Litres poured into the current fill.</param>
/// <param name="TotalQuantityKg">The same by weight.</param>
/// <param name="AvailableQuantityLitres">
/// What a bowser can still draw: the current fill, less anything already dispatched off it.
/// </param>
/// <param name="ConsignmentCount">How many consignments make up the current fill.</param>
/// <param name="FillNumber">Which fill the tank is on; a dispatch that empties it opens the next.</param>
/// <param name="LastClosedAtUtc">When a dispatch last closed a fill of this tank, if one has.</param>
public sealed record TankView(
    string Code,
    string Name,
    decimal CapacityLitres,
    decimal TotalQuantityLitres,
    decimal TotalQuantityKg,
    decimal AvailableQuantityLitres,
    int ConsignmentCount,
    int FillNumber,
    DateTime? LastClosedAtUtc,
    TankStatus Status,
    TankTemperatureView? LatestTemperature);

/// <summary>One temperature taken against a tank (SCRUM-52).</summary>
/// <param name="Celsius">The reading.</param>
/// <param name="RecordedBy">Who took it.</param>
/// <param name="RecordedAtUtc">When it was taken.</param>
/// <param name="FillNumber">The fill the tank was on at the time.</param>
public sealed record TankTemperatureView(
    decimal Celsius,
    string? RecordedBy,
    DateTime RecordedAtUtc,
    int FillNumber);

/// <summary>The details a tank is created or amended with (SCRUM-52).</summary>
public sealed record SaveTankCommand(string Code, string Name, decimal CapacityLitres);

/// <summary>One consignment on a tank's manifest.</summary>
/// <param name="ConsignmentReference">The consignment's gate reference.</param>
/// <param name="SocietyCode">Code of the supplying society.</param>
/// <param name="SocietyName">Name of the supplying society.</param>
/// <param name="CanLabels">The can labels that made up the consignment.</param>
/// <param name="QuantityLitres">Litres poured.</param>
/// <param name="QuantityKg">Kilograms poured.</param>
/// <param name="PouredAtUtc">When the pour was confirmed.</param>
/// <param name="PouredBy">Officer who confirmed it.</param>
/// <param name="FillNumber">
/// The fill this pour joined, so a reader can tell the load in the tank now from the ones that
/// have already gone to the factory.
/// </param>
public sealed record ManifestEntryView(
    string ConsignmentReference,
    string SocietyCode,
    string SocietyName,
    IReadOnlyList<string> CanLabels,
    decimal QuantityLitres,
    decimal QuantityKg,
    DateTime PouredAtUtc,
    string? PouredBy,
    int FillNumber);

/// <summary>A tank's manifest, with the totals of the load it is holding now.</summary>
/// <param name="Tank">The tank the manifest is for.</param>
/// <param name="Entries">Every consignment poured into the tank, newest pour first.</param>
public sealed record TankManifestView(TankView Tank, IReadOnlyList<ManifestEntryView> Entries);

/// <summary>A consignment that has passed the gate and has not yet been poured.</summary>
public sealed record PourableConsignmentView(
    string Reference,
    string SocietyCode,
    string SocietyName,
    decimal TotalQuantityLitres,
    decimal TotalQuantityKg);

/// <summary>Chilling tanks and their manifests (SCRUM-52).</summary>
public interface ITankService
{
    /// <summary>Lists the tanks with their running totals.</summary>
    Task<IReadOnlyList<TankView>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Consignments the officer may pour: accepted at the gate and not already in a tank.
    /// </summary>
    Task<IReadOnlyList<PourableConsignmentView>> PourableAsync(CancellationToken cancellationToken = default);

    /// <summary>Records an accepted consignment being poured into a tank.</summary>
    Task<TankManifestView> PourAsync(
        string tankCode,
        string consignmentReference,
        string? pouredBy,
        CancellationToken cancellationToken = default);

    /// <summary>Reads a tank's manifest, optionally for a single pour date.</summary>
    Task<TankManifestView?> ManifestAsync(
        string tankCode,
        DateOnly? pourDate = null,
        CancellationToken cancellationToken = default);

    /// <summary>Adds a tank to the centre.</summary>
    Task<TankView> CreateAsync(SaveTankCommand command, CancellationToken cancellationToken = default);

    /// <summary>Renames a tank and restates its working volume.</summary>
    Task<TankView> UpdateAsync(
        string tankCode,
        SaveTankCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>Takes a tank out of service, or puts it back.</summary>
    Task<TankView> ChangeStatusAsync(
        string tankCode,
        TankStatus status,
        CancellationToken cancellationToken = default);

    /// <summary>Records a temperature against a tank.</summary>
    Task<TankTemperatureView> RecordTemperatureAsync(
        string tankCode,
        decimal celsius,
        string? recordedBy,
        CancellationToken cancellationToken = default);

    /// <summary>The readings taken against a tank, newest first.</summary>
    Task<IReadOnlyList<TankTemperatureView>?> TemperaturesAsync(
        string tankCode,
        int limit = 20,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="ITankService" />
public sealed class TankService : ITankService
{
    private readonly MccIntakeDbContext _dbContext;
    private readonly IIntakeClock _clock;

    public TankService(MccIntakeDbContext dbContext, IIntakeClock clock)
    {
        _dbContext = dbContext;
        _clock = clock;
    }

    public async Task<IReadOnlyList<TankView>> ListAsync(CancellationToken cancellationToken = default)
    {
        var tanks = await _dbContext.ChillingTanks
            .AsNoTracking()
            .OrderBy(tank => tank.Code)
            .ToListAsync(cancellationToken);

        var views = new List<TankView>(tanks.Count);

        foreach (var tank in tanks)
        {
            views.Add(await ToViewAsync(tank, cancellationToken));
        }

        return views;
    }

    public async Task<IReadOnlyList<PourableConsignmentView>> PourableAsync(
        CancellationToken cancellationToken = default)
    {
        // Accepted at the gate, and not already sitting in a tank. Anything else is not offered,
        // so the officer cannot select milk that was rejected or never tested.
        return await _dbContext.Consignments
            .AsNoTracking()
            .Include(consignment => consignment.Society)
            .Where(consignment => consignment.Status == ConsignmentStatus.Accepted)
            .Where(consignment => !_dbContext.TankPours.Any(pour => pour.ConsignmentId == consignment.Id))
            .OrderBy(consignment => consignment.Reference)
            .Select(consignment => new PourableConsignmentView(
                consignment.Reference,
                consignment.Society!.Code,
                consignment.Society.Name,
                consignment.TotalQuantityLitres,
                consignment.TotalQuantityKg))
            .ToListAsync(cancellationToken);
    }

    public async Task<TankManifestView> PourAsync(
        string tankCode,
        string consignmentReference,
        string? pouredBy,
        CancellationToken cancellationToken = default)
    {
        var tank = await FindTankAsync(tankCode, cancellationToken)
            ?? throw new EntityNotFoundException("ChillingTank", tankCode);

        var consignment = await _dbContext.Consignments
            .FirstOrDefaultAsync(candidate => candidate.Reference == consignmentReference, cancellationToken)
            ?? throw new EntityNotFoundException("Consignment", consignmentReference);

        // A consignment goes into exactly one tank. The unique index is what settles a race
        // between two officers pouring the same milk; this check only gives a better message.
        if (await _dbContext.TankPours.AnyAsync(pour => pour.ConsignmentId == consignment.Id, cancellationToken))
        {
            throw new ConsignmentAlreadyPouredException(consignmentReference);
        }

        // One reading of the clock, converted, so the instant and the day it is filed under
        // cannot straddle a second boundary.
        var pouredAtUtc = _clock.UtcNow;

        _dbContext.TankPours.Add(TankPour.Pour(
            Guid.NewGuid(),
            tank,
            consignment,
            pouredBy,
            pouredAtUtc,
            _clock.ToLocal(pouredAtUtc)));
        await _dbContext.SaveChangesAsync(cancellationToken);

        return (await ManifestAsync(tank.Code, null, cancellationToken))!;
    }

    public async Task<TankManifestView?> ManifestAsync(
        string tankCode,
        DateOnly? pourDate = null,
        CancellationToken cancellationToken = default)
    {
        var tank = await FindTankAsync(tankCode, cancellationToken);

        if (tank is null)
        {
            return null;
        }

        var pours = _dbContext.TankPours
            .AsNoTracking()
            .Include(pour => pour.Consignment)!
                .ThenInclude(consignment => consignment!.Society)
            .Include(pour => pour.Consignment)!
                .ThenInclude(consignment => consignment!.Cans)
            .Where(pour => pour.TankId == tank.Id);

        if (pourDate is { } date)
        {
            pours = pours.Where(pour => pour.PourDate == date);
        }

        var entries = await pours
            .OrderByDescending(pour => pour.PouredAtUtc)
            .ToListAsync(cancellationToken);

        return new TankManifestView(
            // The tank totals are always the whole tank, even when the manifest is filtered to a
            // day: what the tank holds does not change because of how it is being looked at.
            await ToViewAsync(tank, cancellationToken),
            entries.Select(pour => new ManifestEntryView(
                pour.Consignment!.Reference,
                pour.Consignment.Society?.Code ?? string.Empty,
                pour.Consignment.Society?.Name ?? string.Empty,
                pour.Consignment.Cans.OrderBy(can => can.CanNumber).Select(can => can.CanLabel).ToList(),
                pour.QuantityLitres,
                pour.QuantityKg,
                pour.PouredAtUtc,
                pour.PouredBy,
                pour.FillNumber)).ToList());
    }

    public async Task<TankView> CreateAsync(
        SaveTankCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var code = command.Code?.Trim().ToUpperInvariant() ?? string.Empty;

        if (await _dbContext.ChillingTanks.AnyAsync(tank => tank.Code == code, cancellationToken))
        {
            throw new DuplicateCodeException("ChillingTank", code);
        }

        var tank = new ChillingTank(Guid.NewGuid(), code, command.Name, command.CapacityLitres);

        _dbContext.ChillingTanks.Add(tank);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return await ToViewAsync(tank, cancellationToken);
    }

    public async Task<TankView> UpdateAsync(
        string tankCode,
        SaveTankCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var tank = await FindTankAsync(tankCode, cancellationToken)
            ?? throw new EntityNotFoundException("ChillingTank", tankCode);

        // The code is painted on the plant and named on every pour and dispatch note the tank has
        // ever carried, so it is not something a rename may change.
        tank.Describe(command.Name, command.CapacityLitres);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return await ToViewAsync(tank, cancellationToken);
    }

    public async Task<TankView> ChangeStatusAsync(
        string tankCode,
        TankStatus status,
        CancellationToken cancellationToken = default)
    {
        var tank = await FindTankAsync(tankCode, cancellationToken)
            ?? throw new EntityNotFoundException("ChillingTank", tankCode);

        var view = await ToViewAsync(tank, cancellationToken);

        tank.ChangeStatus(status, view.AvailableQuantityLitres);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return await ToViewAsync(tank, cancellationToken);
    }

    public async Task<TankTemperatureView> RecordTemperatureAsync(
        string tankCode,
        decimal celsius,
        string? recordedBy,
        CancellationToken cancellationToken = default)
    {
        var tank = await FindTankAsync(tankCode, cancellationToken)
            ?? throw new EntityNotFoundException("ChillingTank", tankCode);

        var recordedAtUtc = _clock.UtcNow;

        var reading = TankTemperatureReading.Record(
            Guid.NewGuid(),
            tank,
            celsius,
            recordedBy,
            recordedAtUtc,
            _clock.ToLocal(recordedAtUtc));

        _dbContext.TankTemperatureReadings.Add(reading);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return new TankTemperatureView(
            reading.Celsius,
            reading.RecordedBy,
            reading.RecordedAtUtc,
            reading.FillNumber);
    }

    public async Task<IReadOnlyList<TankTemperatureView>?> TemperaturesAsync(
        string tankCode,
        int limit = 20,
        CancellationToken cancellationToken = default)
    {
        var tank = await FindTankAsync(tankCode, cancellationToken);

        if (tank is null)
        {
            return null;
        }

        return await ReadingsFor(tank.Id).Take(Math.Clamp(limit, 1, 200)).ToListAsync(cancellationToken);
    }

    private IQueryable<TankTemperatureView> ReadingsFor(Guid tankId) =>
        _dbContext.TankTemperatureReadings
            .AsNoTracking()
            .Where(reading => reading.TankId == tankId)
            .OrderByDescending(reading => reading.RecordedAtUtc)
            .Select(reading => new TankTemperatureView(
                reading.Celsius,
                reading.RecordedBy,
                reading.RecordedAtUtc,
                reading.FillNumber));

    private async Task<ChillingTank?> FindTankAsync(string tankCode, CancellationToken cancellationToken)
    {
        var code = tankCode?.Trim().ToUpperInvariant() ?? string.Empty;

        return await _dbContext.ChillingTanks.FirstOrDefaultAsync(tank => tank.Code == code, cancellationToken);
    }

    /// <summary>
    /// Totals a tank from the pours of its current fill, so the running total cannot drift from
    /// the manifest — nor report milk that has already left on a bowser.
    /// </summary>
    private async Task<TankView> ToViewAsync(ChillingTank tank, CancellationToken cancellationToken)
    {
        var totals = await _dbContext.TankPours
            .AsNoTracking()
            .Where(pour => pour.TankId == tank.Id && pour.FillNumber == tank.FillNumber)
            .GroupBy(pour => 1)
            .Select(group => new
            {
                Litres = group.Sum(pour => pour.QuantityLitres),
                Kg = group.Sum(pour => pour.QuantityKg),
                Count = group.Count()
            })
            .FirstOrDefaultAsync(cancellationToken);

        // A partial draw leaves the fill open with a balance, so what can still be dispatched is
        // read the same way the dispatch service reads it rather than assumed to be the total.
        var dispatched = await _dbContext.DispatchSources
            .AsNoTracking()
            .Where(source => source.TankId == tank.Id && source.FillNumber == tank.FillNumber)
            .SumAsync(source => (decimal?)source.QuantityLitres, cancellationToken) ?? 0m;

        var poured = totals?.Litres ?? 0m;

        var latest = await ReadingsFor(tank.Id).FirstOrDefaultAsync(cancellationToken);

        return new TankView(
            tank.Code,
            tank.Name,
            tank.CapacityLitres,
            poured,
            totals?.Kg ?? 0m,
            poured - dispatched,
            totals?.Count ?? 0,
            tank.FillNumber,
            tank.LastClosedAtUtc,
            tank.Status,
            latest);
    }
}
