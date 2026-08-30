using MccIntakeService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace MccIntakeService.Tests.Infrastructure;

/// <summary>
/// The rest of the suite runs on SQLite, which materialises a <see cref="DateOnly"/> without
/// complaint. MySQL — the only provider staging and production run on — cannot, so a mapping that
/// broke every read of a consignment still passed CI. Building the model against the MySQL provider
/// needs no server, which puts that mapping under test here rather than leaving it to QA.
/// </summary>
public class DateOnlyMappingTests
{
    [Fact]
    public void Every_date_is_stored_as_a_DateTime_because_MySQL_cannot_read_a_DateOnly()
    {
        var dates = DateProperties();

        Assert.NotEmpty(dates);

        foreach (var date in dates)
        {
            var converter = date.GetValueConverter();

            Assert.True(
                converter?.ProviderClrType == typeof(DateTime),
                $"{date.DeclaringType.DisplayName()}.{date.Name} is read back as "
                + $"{converter?.ProviderClrType.Name ?? nameof(DateOnly)}, which MySqlDataReader "
                + "cannot supply. Every query touching the entity throws InvalidCastException.");
        }
    }

    [Fact]
    public void Every_date_keeps_its_date_column_so_the_migrated_schema_still_matches()
    {
        foreach (var date in DateProperties())
        {
            Assert.Equal("date", date.GetColumnType());
        }
    }

    /// <summary>
    /// The model is built, not connected to: the credentials are never used. A real server would
    /// tie these tests to one being up, which is what left the mapping uncovered to begin with.
    /// </summary>
    private static IReadOnlyList<IProperty> DateProperties()
    {
        var options = new DbContextOptionsBuilder<MccIntakeDbContext>()
            .UseMySQL("Server=localhost;Database=mcc_intake;User=unused;Password=unused")
            .Options;

        using var context = new MccIntakeDbContext(options);

        return context.Model
            .GetEntityTypes()
            .SelectMany(entity => entity.GetProperties())
            .Where(property =>
                property.ClrType == typeof(DateOnly) || property.ClrType == typeof(DateOnly?))
            .ToList();
    }
}
