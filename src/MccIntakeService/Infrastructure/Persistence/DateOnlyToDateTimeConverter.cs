using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace MccIntakeService.Infrastructure.Persistence;

/// <summary>
/// Stores a <see cref="DateOnly"/> as a <see cref="DateTime"/> at midnight, and reads it back.
/// </summary>
/// <remarks>
/// Oracle's <c>MySql.EntityFrameworkCore</c> maps <see cref="DateOnly"/> to a <c>date</c> column and
/// then asks its reader for a <see cref="DateOnly"/>, which <c>MySqlDataReader</c> cannot supply:
/// every read of an entity holding one threw
/// <c>InvalidCastException: Unable to cast object of type 'System.DateTime' to type 'System.DateOnly'</c>.
/// Writes were unaffected, so the failure only surfaced when a row was loaded back.
/// <para>
/// Going through <see cref="DateTime"/> keeps the reader on a type it does support. The column stays
/// <c>date</c> (see <see cref="MccIntakeDbContext.ConfigureConventions"/>), so the schema is unchanged
/// and no migration is needed.
/// </para>
/// <para>
/// The alternative was Pomelo, which materialises <see cref="DateOnly"/> natively; its newest release
/// (9.0.0) targets EF Core 9 and this solution is on EF Core 10, so that swap would mean downgrading
/// EF Core across every project. This conversion is contained to the mapping layer instead.
/// </para>
/// </remarks>
public sealed class DateOnlyToDateTimeConverter : ValueConverter<DateOnly, DateTime>
{
    public DateOnlyToDateTimeConverter()
        : base(
            date => date.ToDateTime(TimeOnly.MinValue),
            value => DateOnly.FromDateTime(value))
    {
    }
}
