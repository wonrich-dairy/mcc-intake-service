using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MccIntakeService.Application.Societies;
using MccIntakeService.Tests.Support;
using Wonrich.Auth.Authorization;

namespace MccIntakeService.Tests.Api;

public class SocietiesApiTests : IClassFixture<IntakeApiFactoryFixture>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IntakeApiFactory _factory;

    public SocietiesApiTests(IntakeApiFactoryFixture fixture)
    {
        _factory = fixture.Factory;
    }

    [Fact]
    public async Task The_seeded_societies_are_offered_for_selection()
    {
        var client = _factory.CreateClientAs(WonrichRoles.IntakeOfficer);

        var societies = await client.GetFromJsonAsync<List<SocietyView>>("/api/societies", JsonOptions);

        Assert.NotNull(societies);
        Assert.Equal(["BD", "KC", "MT", "NW"], societies.Select(society => society.Code));
    }

    [Fact]
    public async Task A_single_society_can_be_fetched_by_identifier()
    {
        var client = _factory.CreateClientAs(WonrichRoles.IntakeOfficer);

        var societies = await client.GetFromJsonAsync<List<SocietyView>>("/api/societies", JsonOptions);
        var expected = societies!.Single(society => society.Code == "KC");

        var society = await client.GetFromJsonAsync<SocietyView>($"/api/societies/{expected.Id}", JsonOptions);

        Assert.NotNull(society);
        Assert.Equal("KC", society.Code);
        Assert.Equal("KC", society.CanLabelPrefix);
    }

    [Fact]
    public async Task Fetching_an_unknown_society_returns_404()
    {
        var client = _factory.CreateClientAs(WonrichRoles.IntakeOfficer);

        var response = await client.GetAsync($"/api/societies/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_malformed_society_identifier_does_not_match_the_route()
    {
        var client = _factory.CreateClientAs(WonrichRoles.IntakeOfficer);

        var response = await client.GetAsync("/api/societies/not-a-guid");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
