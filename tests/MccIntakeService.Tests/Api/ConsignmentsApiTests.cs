using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using MccIntakeService.Application.Consignments;
using MccIntakeService.Application.Societies;
using MccIntakeService.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace MccIntakeService.Tests.Api;

public class ConsignmentsApiTests : IClassFixture<IntakeApiFactoryFixture>
{
    /// <summary>Mirrors the serialisation the API is configured with, enum names included.</summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly IntakeApiFactory _factory;

    public ConsignmentsApiTests(IntakeApiFactoryFixture fixture)
    {
        _factory = fixture.Factory;
    }

    private async Task<Guid> KandySocietyIdAsync(HttpClient client)
    {
        var societies = await client.GetFromJsonAsync<List<SocietyView>>("/api/societies", JsonOptions);

        return societies!.Single(society => society.Code == "KC").Id;
    }

    private static object CanSheet(Guid societyId, DateTime? arrival = null) => new
    {
        societyId,
        arrivalAtLocal = arrival,
        cans = new[]
        {
            new { canNumber = 1, quantityKg = 40.5m },
            new { canNumber = 2, quantityKg = 39.5m }
        }
    };

    [Fact]
    public async Task Registering_a_consignment_returns_201_with_a_location_header()
    {
        var client = _factory.CreateClient();
        var societyId = await KandySocietyIdAsync(client);

        var response = await client.PostAsJsonAsync("/api/consignments", CanSheet(societyId));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var created = await response.Content.ReadFromJsonAsync<ConsignmentView>(JsonOptions);

        Assert.NotNull(created);
        Assert.StartsWith("MCC-", created.Reference, StringComparison.Ordinal);
        Assert.Equal(80m, created.TotalQuantityKg);
        Assert.Equal(["KC 01", "KC 02"], created.Cans.Select(can => can.CanLabel));
        Assert.Contains(created.Reference, response.Headers.Location!.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_registered_consignment_can_be_fetched_from_the_location_it_reports()
    {
        var client = _factory.CreateClient();
        var societyId = await KandySocietyIdAsync(client);

        var created = await client.PostAsJsonAsync("/api/consignments", CanSheet(societyId));
        var location = created.Headers.Location!;

        var fetched = await client.GetFromJsonAsync<ConsignmentView>(location, JsonOptions);

        Assert.NotNull(fetched);
        Assert.Equal("KC", fetched.SocietyCode);
    }

    [Fact]
    public async Task The_status_is_serialised_as_a_name_rather_than_a_number()
    {
        var client = _factory.CreateClient();
        var societyId = await KandySocietyIdAsync(client);

        var response = await client.PostAsJsonAsync("/api/consignments", CanSheet(societyId));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Contains("\"status\":\"Registered\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Submitting_without_any_cans_is_rejected_with_400()
    {
        var client = _factory.CreateClient();
        var societyId = await KandySocietyIdAsync(client);

        var response = await client.PostAsJsonAsync(
            "/api/consignments",
            new { societyId, cans = Array.Empty<object>() });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("at least one can", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Submitting_a_can_with_no_quantity_is_rejected_with_400()
    {
        var client = _factory.CreateClient();
        var societyId = await KandySocietyIdAsync(client);

        var response = await client.PostAsJsonAsync("/api/consignments", new
        {
            societyId,
            cans = new[] { new { canNumber = 1, quantityKg = 0m } }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Submitting_a_society_that_is_not_registered_is_rejected_with_422()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/consignments", CanSheet(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("entity_not_found", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task An_arrival_time_in_the_future_is_rejected_with_400()
    {
        var client = _factory.CreateClient();
        var societyId = await KandySocietyIdAsync(client);

        // The shared clock sits at 08:00, so an afternoon arrival has not happened yet.
        var response = await client.PostAsJsonAsync(
            "/api/consignments",
            CanSheet(societyId, new DateTime(2026, 8, 23, 17, 15, 0)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("domain_validation_failed", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Arriving_after_the_cutoff_is_rejected_with_422_and_a_message_stating_the_cutoff()
    {
        // Its own host, so moving the clock past the cutoff cannot disturb the other API tests.
        using var factory = new IntakeApiFactory();
        factory.Clock.LocalNow = new DateTime(2026, 8, 23, 17, 20, 0);

        var client = factory.CreateClient();
        var societyId = await KandySocietyIdAsync(client);

        var lateArrival = new DateTime(2026, 8, 23, 17, 15, 0);

        var response = await client.PostAsJsonAsync("/api/consignments", CanSheet(societyId, lateArrival));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("intake_cutoff_exceeded", problem.GetProperty("code").GetString());
        Assert.Equal("16:00", problem.GetProperty("cutoff").GetString());
        Assert.Equal("17:15", problem.GetProperty("arrivalTime").GetString());
        Assert.Contains("16:00", problem.GetProperty("detail").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Fetching_an_unknown_reference_returns_404()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/consignments/MCC-20260823-KC-99");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Consignments_can_be_listed_and_filtered_over_http()
    {
        var client = _factory.CreateClient();
        var societyId = await KandySocietyIdAsync(client);

        await client.PostAsJsonAsync("/api/consignments", CanSheet(societyId));

        var page = await client.GetFromJsonAsync<PagedResult<ConsignmentView>>(
            $"/api/consignments?societyCode=KC&date=2026-08-23&page=1&pageSize=10",
            JsonOptions);

        Assert.NotNull(page);
        Assert.NotEmpty(page.Items);
        Assert.All(page.Items, consignment => Assert.Equal("KC", consignment.SocietyCode));
    }

    [Fact]
    public async Task Every_registered_consignment_is_persisted_with_its_cans()
    {
        var client = _factory.CreateClient();
        var societyId = await KandySocietyIdAsync(client);

        var response = await client.PostAsJsonAsync("/api/consignments", CanSheet(societyId));
        var created = await response.Content.ReadFromJsonAsync<ConsignmentView>(JsonOptions);

        await _factory.WithDbContextAsync(async context =>
        {
            var stored = await context.Consignments
                .AsNoTracking()
                .SingleAsync(consignment => consignment.Reference == created!.Reference);

            Assert.Equal(2, stored.Cans.Count);
            Assert.Equal(80m, stored.TotalQuantityKg);
        });
    }

    [Fact]
    public async Task The_swagger_document_lists_the_consignment_endpoints()
    {
        var client = _factory.CreateClient();

        var document = await client.GetStringAsync("/swagger/v1/swagger.json");

        Assert.Contains("/api/consignments", document, StringComparison.Ordinal);
        Assert.Contains("/api/societies", document, StringComparison.Ordinal);
    }
}

/// <summary>Shares one hosted application across the API tests.</summary>
public sealed class IntakeApiFactoryFixture : IDisposable
{
    internal IntakeApiFactory Factory { get; } = new();

    public void Dispose() => Factory.Dispose();
}
