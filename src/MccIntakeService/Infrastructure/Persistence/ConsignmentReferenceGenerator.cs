using System.Globalization;
using MccIntakeService.Application.Consignments;
using MccIntakeService.Domain.Societies;
using Microsoft.EntityFrameworkCore;

namespace MccIntakeService.Infrastructure.Persistence;

/// <inheritdoc cref="IConsignmentReferenceGenerator" />
public sealed class ConsignmentReferenceGenerator : IConsignmentReferenceGenerator
{
    private const string ReferencePrefix = "MCC";

    private readonly MccIntakeDbContext _dbContext;

    public ConsignmentReferenceGenerator(MccIntakeDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<string> NextAsync(
        Society society,
        DateOnly arrivalDate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(society);

        var prefix = BuildPrefix(society.Code, arrivalDate);

        var referencesForDay = await _dbContext.Consignments
            .AsNoTracking()
            .Where(consignment => consignment.SocietyId == society.Id && consignment.ArrivalDate == arrivalDate)
            .Select(consignment => consignment.Reference)
            .ToListAsync(cancellationToken);

        // Read the highest sequence already issued rather than counting rows, so a deleted or
        // failed registration never causes a reference to be handed out twice.
        var highestSequence = referencesForDay
            .Select(reference => ParseSequence(reference, prefix))
            .DefaultIfEmpty(0)
            .Max();

        return prefix + (highestSequence + 1).ToString("D2", CultureInfo.InvariantCulture);
    }

    internal static string BuildPrefix(string societyCode, DateOnly arrivalDate) =>
        $"{ReferencePrefix}-{arrivalDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}-{societyCode}-";

    private static int ParseSequence(string reference, string prefix)
    {
        if (!reference.StartsWith(prefix, StringComparison.Ordinal))
        {
            return 0;
        }

        var suffix = reference[prefix.Length..];

        return int.TryParse(suffix, NumberStyles.None, CultureInfo.InvariantCulture, out var sequence)
            ? sequence
            : 0;
    }
}
