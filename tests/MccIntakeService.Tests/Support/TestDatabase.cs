using MccIntakeService.Domain.Societies;
using MccIntakeService.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MccIntakeService.Tests.Support;

/// <summary>
/// An isolated SQLite database held open for the lifetime of one test. SQLite is used rather than
/// the in-memory provider because these tests depend on real relational behaviour — most of all the
/// unique index that guards consignment references.
/// </summary>
internal sealed class TestDatabase : IDisposable
{
    private readonly SqliteConnection _connection;

    private TestDatabase(SqliteConnection connection)
    {
        _connection = connection;
    }

    /// <summary>The seeded societies, in the order the seed declares them.</summary>
    public IReadOnlyList<Society> SeededSocieties { get; private set; } = [];

    public static TestDatabase Create()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var database = new TestDatabase(connection);

        using var context = database.CreateContext();
        context.Database.EnsureCreated();
        database.SeededSocieties = context.Societies.OrderBy(society => society.Code).ToList();

        return database;
    }

    public MccIntakeDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<MccIntakeDbContext>()
            .UseSqlite(_connection)
            .ConfigureWarnings(warnings => warnings.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.AmbientTransactionWarning))
            .EnableSensitiveDataLogging()
            .Options;

        return new MccIntakeDbContext(options);
    }

    /// <summary>Fetches a seeded society by its code, e.g. "KC".</summary>
    public Society Society(string code) =>
        SeededSocieties.Single(society => society.Code == code);

    public void Dispose() => _connection.Dispose();
}
