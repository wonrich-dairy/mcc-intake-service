using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using MccIntakeService.Application.Consignments;
using MccIntakeService.Application.Societies;
using MccIntakeService.Tests.Support;

namespace MccIntakeService.Tests.Api;

/// <summary>Drives the kilogram-based can sheet over HTTP.</summary>
public class CanQuantityApiTests
{
    /// <summary>Mirrors the host's serialisation: the consignment status travels as its name.</summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public async Task A_can_sheet_is_submitted_in_kilograms_and_answered_with_both_units()
    {
        using var factory = new IntakeApiFactory();
        var client = factory.CreateClient();

        var societies = await client.GetFromJsonAsync<List<SocietyView>>("/api/societies", JsonOptions);

        var response = await client.PostAsJsonAsync("/api/consignments", new
        {
            societyId = societies!.First().Id,
            cans = new[] { new { canNumber = 1, quantityKg = 41.20m } }
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var created = await response.Content.ReadFromJsonAsync<ConsignmentView>(JsonOptions);
        var can = Assert.Single(created!.Cans);

        Assert.Equal(41.20m, can.QuantityKg);
        Assert.Equal(40.00m, can.QuantityLitres);
        Assert.Equal(41.20m, created.TotalQuantityKg);
        Assert.Equal(40.00m, created.TotalQuantityLitres);
    }

    [Fact]
    public async Task A_can_weight_beyond_the_limit_is_refused()
    {
        using var factory = new IntakeApiFactory();
        var client = factory.CreateClient();

        var societies = await client.GetFromJsonAsync<List<SocietyView>>("/api/societies", JsonOptions);

        var response = await client.PostAsJsonAsync("/api/consignments", new
        {
            societyId = societies!.First().Id,
            cans = new[] { new { canNumber = 1, quantityKg = 1000.01m } }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task The_documented_can_sheet_asks_for_kilograms_not_litres()
    {
        using var factory = new IntakeApiFactory();
        var client = factory.CreateClient();

        var swagger = JsonDocument.Parse(await client.GetStringAsync("/swagger/v1/swagger.json")).RootElement;

        var can = swagger
            .GetProperty("components")
            .GetProperty("schemas")
            .GetProperty("RegisterConsignmentCanRequest")
            .GetProperty("properties");

        Assert.True(can.TryGetProperty("quantityKg", out _));
        Assert.False(can.TryGetProperty("quantityLitres", out _));
    }
}
