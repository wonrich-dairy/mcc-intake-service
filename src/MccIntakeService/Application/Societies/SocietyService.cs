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
        bool activeOnly = true,
        CancellationToken cancellationToken = default)
    {
        var societies = _dbContext.Societies.AsNoTracking().AsQueryable();

        if (activeOnly)
        {
            societies = societies.Where(society => society.IsActive);
        }

        return await societies
            .OrderBy(society => society.Code)
            .Select(society => new SocietyView(
                society.Id,
                society.Code,
                society.Name,
                society.CanLabelPrefix,
                society.IsActive))
            .ToListAsync(cancellationToken);
    }

    public async Task<SocietyView?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Societies
            .AsNoTracking()
            .Where(society => society.Id == id)
            .Select(society => new SocietyView(
                society.Id,
                society.Code,
                society.Name,
                society.CanLabelPrefix,
                society.IsActive))
            .FirstOrDefaultAsync(cancellationToken);
    }
}
