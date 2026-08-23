using MccIntakeService.Application.Societies;
using MccIntakeService.Tests.Support;

namespace MccIntakeService.Tests.Application;

public class SocietyServiceTests : IDisposable
{
    private readonly TestDatabase _database = TestDatabase.Create();

    [Fact]
    public async Task Societies_are_listed_in_code_order_for_selection_at_the_gate()
    {
        await using var context = _database.CreateContext();
        var service = new SocietyService(context);

        var societies = await service.ListAsync();

        Assert.Equal(["BD", "KC", "MT", "NW"], societies.Select(society => society.Code));
        Assert.All(societies, society => Assert.True(society.IsActive));
    }

    [Fact]
    public async Task A_society_carries_the_can_label_prefix_the_officer_will_see_on_the_cans()
    {
        await using var context = _database.CreateContext();
        var service = new SocietyService(context);

        var societies = await service.ListAsync();
        var kandy = societies.Single(society => society.Code == "KC");

        Assert.Equal("KC", kandy.CanLabelPrefix);
        Assert.Equal("Kandy Co-operative Dairy Society", kandy.Name);
    }

    [Fact]
    public async Task A_single_society_can_be_fetched_by_identifier()
    {
        await using var context = _database.CreateContext();
        var service = new SocietyService(context);

        var society = await service.GetAsync(_database.Society("MT").Id);

        Assert.NotNull(society);
        Assert.Equal("MT", society.Code);
    }

    [Fact]
    public async Task Fetching_an_unknown_society_yields_nothing()
    {
        await using var context = _database.CreateContext();
        var service = new SocietyService(context);

        Assert.Null(await service.GetAsync(Guid.NewGuid()));
    }

    public void Dispose()
    {
        _database.Dispose();
        GC.SuppressFinalize(this);
    }
}
