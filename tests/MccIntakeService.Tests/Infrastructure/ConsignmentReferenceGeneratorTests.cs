using MccIntakeService.Domain.Consignments;
using MccIntakeService.Infrastructure.Persistence;
using MccIntakeService.Tests.Support;

namespace MccIntakeService.Tests.Infrastructure;

public class ConsignmentReferenceGeneratorTests : IDisposable
{
    private readonly TestDatabase _database = TestDatabase.Create();

    private static readonly DateOnly IntakeDate = new(2026, 8, 23);

    [Fact]
    public async Task The_first_consignment_of_the_day_is_numbered_01()
    {
        await using var context = _database.CreateContext();
        var generator = new ConsignmentReferenceGenerator(context);

        var reference = await generator.NextAsync(_database.Society("KC"), IntakeDate);

        Assert.Equal("MCC-20260823-KC-01", reference);
    }

    [Fact]
    public async Task Each_further_consignment_for_the_same_society_and_day_increments_the_sequence()
    {
        await SeedConsignmentAsync("KC", IntakeDate, "MCC-20260823-KC-01");
        await SeedConsignmentAsync("KC", IntakeDate, "MCC-20260823-KC-02");

        await using var context = _database.CreateContext();
        var generator = new ConsignmentReferenceGenerator(context);

        Assert.Equal("MCC-20260823-KC-03", await generator.NextAsync(_database.Society("KC"), IntakeDate));
    }

    [Fact]
    public async Task The_sequence_is_scoped_to_one_society()
    {
        await SeedConsignmentAsync("KC", IntakeDate, "MCC-20260823-KC-01");

        await using var context = _database.CreateContext();
        var generator = new ConsignmentReferenceGenerator(context);

        Assert.Equal("MCC-20260823-MT-01", await generator.NextAsync(_database.Society("MT"), IntakeDate));
    }

    [Fact]
    public async Task The_sequence_restarts_on_the_next_intake_day()
    {
        await SeedConsignmentAsync("KC", IntakeDate, "MCC-20260823-KC-01");

        await using var context = _database.CreateContext();
        var generator = new ConsignmentReferenceGenerator(context);

        var nextDay = IntakeDate.AddDays(1);

        Assert.Equal("MCC-20260824-KC-01", await generator.NextAsync(_database.Society("KC"), nextDay));
    }

    [Fact]
    public async Task A_gap_left_by_a_removed_consignment_is_not_reissued()
    {
        await SeedConsignmentAsync("KC", IntakeDate, "MCC-20260823-KC-01");
        await SeedConsignmentAsync("KC", IntakeDate, "MCC-20260823-KC-02");

        await using (var context = _database.CreateContext())
        {
            var first = context.Consignments.Single(consignment => consignment.Reference == "MCC-20260823-KC-01");
            context.Consignments.Remove(first);
            await context.SaveChangesAsync();
        }

        await using var reading = _database.CreateContext();
        var generator = new ConsignmentReferenceGenerator(reading);

        // Highest issued is 02, so the next is 03 — never 02 again.
        Assert.Equal("MCC-20260823-KC-03", await generator.NextAsync(_database.Society("KC"), IntakeDate));
    }

    private async Task SeedConsignmentAsync(string societyCode, DateOnly arrivalDate, string reference)
    {
        await using var context = _database.CreateContext();

        var society = context.Societies.Single(candidate => candidate.Code == societyCode);
        var arrival = arrivalDate.ToDateTime(new TimeOnly(7, 0));

        var consignment = Consignment.Register(
            Guid.NewGuid(),
            reference,
            society,
            arrival,
            [new CanEntry(1, 40m)],
            new TimeOnly(16, 0),
            arrival.AddHours(1),
            DateTimeOffset.UtcNow);

        context.Consignments.Add(consignment);
        await context.SaveChangesAsync();
    }

    public void Dispose()
    {
        _database.Dispose();
        GC.SuppressFinalize(this);
    }
}
