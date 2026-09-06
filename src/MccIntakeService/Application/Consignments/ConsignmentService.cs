using MccIntakeService.Application.Abstractions;
using MccIntakeService.Configuration;
using MccIntakeService.Domain.Common;
using MccIntakeService.Domain.Consignments;
using MccIntakeService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MccIntakeService.Application.Consignments;

/// <inheritdoc cref="IConsignmentService" />
public sealed class ConsignmentService : IConsignmentService
{
    /// <summary>
    /// Two intake officers registering for the same society in the same second can compute the same
    /// sequence number. The unique index rejects the loser, so re-read and re-issue a few times.
    /// </summary>
    private const int ReferenceAllocationAttempts = 5;

    private const int MaxPageSize = 200;

    private readonly MccIntakeDbContext _dbContext;
    private readonly IConsignmentReferenceGenerator _referenceGenerator;
    private readonly IIntakeClock _clock;
    private readonly IntakeOptions _options;
    private readonly ILogger<ConsignmentService> _logger;

    public ConsignmentService(
        MccIntakeDbContext dbContext,
        IConsignmentReferenceGenerator referenceGenerator,
        IIntakeClock clock,
        IOptions<IntakeOptions> options,
        ILogger<ConsignmentService> logger)
    {
        _dbContext = dbContext;
        _referenceGenerator = referenceGenerator;
        _clock = clock;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ConsignmentView> RegisterAsync(
        RegisterConsignmentCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        // A society must already exist: the officer picks one, they cannot type a new one in.
        var society = await _dbContext.Societies
            .FirstOrDefaultAsync(candidate => candidate.Id == command.SocietyId, cancellationToken)
            ?? throw new EntityNotFoundException("Society", command.SocietyId.ToString());

        var nowLocal = _clock.LocalNow;
        var arrivalAtLocal = command.ArrivalAtLocal ?? nowLocal;
        var cutoff = _clock.DailyCutoff;

        // Fail fast on a late or future arrival before touching the database for a reference.
        Consignment.EnsureArrivalIsRegistrable(arrivalAtLocal, cutoff, nowLocal);

        var arrivalDate = DateOnly.FromDateTime(arrivalAtLocal);

        for (var attempt = 1; ; attempt++)
        {
            var reference = await _referenceGenerator.NextAsync(society, arrivalDate, cancellationToken);

            var consignment = Consignment.Register(
                Guid.NewGuid(),
                reference,
                society,
                arrivalAtLocal,
                command.Cans,
                _options.MilkDensityKgPerLitre,
                cutoff,
                nowLocal,
                _clock.UtcNow,
                command.RegisteredBy);

            _dbContext.Consignments.Add(consignment);

            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);

                return ToView(consignment, society.Code, society.Name);
            }
            catch (DbUpdateException exception) when (attempt < ReferenceAllocationAttempts)
            {
                Detach(consignment);

                _logger.LogWarning(
                    exception,
                    "Reference {Reference} was already taken while registering a consignment for society {SocietyCode}; retrying (attempt {Attempt} of {MaxAttempts}).",
                    reference,
                    society.Code,
                    attempt,
                    ReferenceAllocationAttempts);
            }
        }
    }

    public async Task<ConsignmentView?> GetByReferenceAsync(
        string reference,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return null;
        }

        var normalised = reference.Trim().ToUpperInvariant();

        var consignment = await _dbContext.Consignments
            .AsNoTracking()
            .Include(candidate => candidate.Society)
            .FirstOrDefaultAsync(candidate => candidate.Reference == normalised, cancellationToken);

        return consignment is null ? null : ToView(consignment);
    }

    public async Task<PagedResult<ConsignmentView>> SearchAsync(
        ConsignmentQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var page = Math.Max(query.Page, 1);
        var pageSize = Math.Clamp(query.PageSize, 1, MaxPageSize);

        var consignments = _dbContext.Consignments
            .AsNoTracking()
            .Include(consignment => consignment.Society)
            .AsQueryable();

        if (query.SocietyId is { } societyId)
        {
            consignments = consignments.Where(consignment => consignment.SocietyId == societyId);
        }

        if (!string.IsNullOrWhiteSpace(query.SocietyCode))
        {
            var societyCode = query.SocietyCode.Trim().ToUpperInvariant();
            consignments = consignments.Where(consignment => consignment.Society!.Code == societyCode);
        }

        if (!string.IsNullOrWhiteSpace(query.Reference))
        {
            var reference = query.Reference.Trim().ToUpperInvariant();
            consignments = consignments.Where(consignment => consignment.Reference == reference);
        }

        if (query.ArrivalDate is { } arrivalDate)
        {
            consignments = consignments.Where(consignment => consignment.ArrivalDate == arrivalDate);
        }

        if (query.FromDate is { } fromDate)
        {
            consignments = consignments.Where(consignment => consignment.ArrivalDate >= fromDate);
        }

        if (query.ToDate is { } toDate)
        {
            consignments = consignments.Where(consignment => consignment.ArrivalDate <= toDate);
        }

        if (query.Status is { } status)
        {
            consignments = consignments.Where(consignment => consignment.Status == status);
        }

        var totalCount = await consignments.CountAsync(cancellationToken);

        var items = await consignments
            .OrderByDescending(consignment => consignment.ArrivalAtLocal)
            .ThenBy(consignment => consignment.Reference)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<ConsignmentView>(
            items.Select(consignment => ToView(consignment)).ToList(),
            page,
            pageSize,
            totalCount);
    }

    /// <summary>Removes a rejected consignment and its cans from the change tracker before retrying.</summary>
    private void Detach(Consignment consignment)
    {
        foreach (var can in consignment.Cans)
        {
            _dbContext.Entry(can).State = EntityState.Detached;
        }

        _dbContext.Entry(consignment).State = EntityState.Detached;
    }

    private static ConsignmentView ToView(
        Consignment consignment,
        string? societyCode = null,
        string? societyName = null)
    {
        var cans = consignment.Cans
            .OrderBy(can => can.CanNumber)
            .Select(can => new ConsignmentCanView(can.CanLabel, can.CanNumber, can.QuantityKg, can.QuantityLitres))
            .ToList();

        return new ConsignmentView(
            consignment.Id,
            consignment.Reference,
            consignment.SocietyId,
            societyCode ?? consignment.Society?.Code ?? string.Empty,
            societyName ?? consignment.Society?.Name ?? string.Empty,
            consignment.ArrivalAtLocal,
            consignment.ArrivalDate,
            consignment.Status,
            consignment.TotalQuantityKg,
            consignment.TotalQuantityLitres,
            cans.Count,
            consignment.RegisteredAtUtc,
            consignment.RegisteredBy,
            cans);
    }
}
