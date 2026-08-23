using System.Linq.Expressions;
using MccIntakeService.Domain.Common;
using MccIntakeService.Domain.Societies;
using MccIntakeService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MccIntakeService.Application.Societies;

/// <inheritdoc cref="ISocietyService" />
public sealed class SocietyService : ISocietyService
{
    private readonly MccIntakeDbContext _dbContext;

    public SocietyService(MccIntakeDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<SocietyView>> ListAsync(
        SocietyQuery? query = null,
        CancellationToken cancellationToken = default)
    {
        query ??= new SocietyQuery();

        var societies = _dbContext.Societies.AsNoTracking().AsQueryable();

        if (query.ActiveOnly)
        {
            societies = societies.Where(society => society.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            // Matched against both name and code so the manager can search either way.
            var term = query.Search.Trim();
            societies = societies.Where(society =>
                EF.Functions.Like(society.Name, $"%{term}%") ||
                EF.Functions.Like(society.Code, $"%{term}%"));
        }

        societies = Order(societies, query);

        return await societies.Select(Projection).ToListAsync(cancellationToken);
    }

    public async Task<SocietyView?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Societies
            .AsNoTracking()
            .Where(society => society.Id == id)
            .Select(Projection)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<SocietyView> CreateAsync(
        CreateSocietyCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var society = new Society(
            Guid.NewGuid(),
            command.Code,
            command.Name,
            command.CanLabelPrefix,
            command.ContactPerson,
            command.ContactNumber);

        await GuardCodeIsFreeAsync(society.Code, excluding: null, cancellationToken);

        _dbContext.Societies.Add(society);
        await SaveGuardingUniqueCodeAsync(society.Code, cancellationToken);

        return ToView(society);
    }

    public async Task<SocietyView> UpdateAsync(
        Guid id,
        UpdateSocietyCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var society = await FindAsync(id, cancellationToken);

        society.UpdateDetails(
            command.Name,
            command.CanLabelPrefix,
            command.ContactPerson,
            command.ContactNumber);

        // The code is frozen once any consignment refers to this society, because the code is
        // baked into every reference already issued.
        var hasConsignments = await _dbContext.Consignments
            .AnyAsync(consignment => consignment.SocietyId == id, cancellationToken);

        society.ChangeCode(command.Code, hasConsignments);

        await GuardCodeIsFreeAsync(society.Code, excluding: id, cancellationToken);
        await SaveGuardingUniqueCodeAsync(society.Code, cancellationToken);

        return ToView(society);
    }

    public async Task<SocietyView> DeactivateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var society = await FindAsync(id, cancellationToken);

        society.Deactivate();
        await _dbContext.SaveChangesAsync(cancellationToken);

        return ToView(society);
    }

    public async Task<SocietyView> ReactivateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var society = await FindAsync(id, cancellationToken);

        society.Reactivate();
        await _dbContext.SaveChangesAsync(cancellationToken);

        return ToView(society);
    }

    private async Task<Society> FindAsync(Guid id, CancellationToken cancellationToken) =>
        await _dbContext.Societies.FirstOrDefaultAsync(society => society.Id == id, cancellationToken)
        ?? throw new EntityNotFoundException("Society", id.ToString());

    /// <summary>Rejects a duplicate code before the round trip, for a message that names the code.</summary>
    private async Task GuardCodeIsFreeAsync(string code, Guid? excluding, CancellationToken cancellationToken)
    {
        var taken = await _dbContext.Societies
            .AsNoTracking()
            .AnyAsync(
                society => society.Code == code && (excluding == null || society.Id != excluding),
                cancellationToken);

        if (taken)
        {
            throw new DuplicateCodeException("Society", code);
        }
    }

    /// <summary>
    /// The pre-check above closes the common case; the unique index is what actually settles a
    /// race between two managers registering the same code at once.
    /// </summary>
    private async Task SaveGuardingUniqueCodeAsync(string code, CancellationToken cancellationToken)
    {
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueCodeViolation(exception))
        {
            throw new DuplicateCodeException("Society", code);
        }
    }

    private static bool IsUniqueCodeViolation(DbUpdateException exception) =>
        exception.InnerException?.Message.Contains("ux_societies_code", StringComparison.OrdinalIgnoreCase) == true
        || exception.InnerException?.Message.Contains("UNIQUE constraint failed: societies.Code", StringComparison.OrdinalIgnoreCase) == true;

    private static IQueryable<Society> Order(IQueryable<Society> societies, SocietyQuery query) =>
        (query.SortBy, query.Descending) switch
        {
            (SocietySortBy.Name, false) => societies.OrderBy(society => society.Name),
            (SocietySortBy.Name, true) => societies.OrderByDescending(society => society.Name),
            (SocietySortBy.IsActive, false) => societies.OrderBy(society => society.IsActive).ThenBy(society => society.Code),
            (SocietySortBy.IsActive, true) => societies.OrderByDescending(society => society.IsActive).ThenBy(society => society.Code),
            (_, true) => societies.OrderByDescending(society => society.Code),
            _ => societies.OrderBy(society => society.Code)
        };

    /// <summary>
    /// Projection shared by the read queries. Held as an expression rather than a method so EF
    /// can translate it into the SELECT list instead of pulling entities back to evaluate it.
    /// </summary>
    private static readonly Expression<Func<Society, SocietyView>> Projection = society => new SocietyView(
        society.Id,
        society.Code,
        society.Name,
        society.CanLabelPrefix,
        society.ContactPerson,
        society.ContactNumber,
        society.IsActive);

    /// <summary>Maps an already-loaded entity, for the write paths that return what they saved.</summary>
    private static SocietyView ToView(Society society) => new(
        society.Id,
        society.Code,
        society.Name,
        society.CanLabelPrefix,
        society.ContactPerson,
        society.ContactNumber,
        society.IsActive);
}
