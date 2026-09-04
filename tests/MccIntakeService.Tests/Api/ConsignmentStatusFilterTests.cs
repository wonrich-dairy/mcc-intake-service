using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using MccIntakeService.Application.Consignments;
using MccIntakeService.Application.Societies;
using MccIntakeService.Tests.Support;
using Wonrich.Auth.Authorization;

namespace MccIntakeService.Tests.Api;

/// <summary>
/// Narrowing the consignment list to one lifecycle state. Without it a client wanting the day's
/// accepted deliveries has to pull every consignment and count them itself, which is fine for one
/// centre's morning and wrong at any volume.
/// </summary>
public class ConsignmentStatusFilterTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private static object SoundPanel(string verdict = "Accept") => new
    {
        fatPercent = 4.1m,
        rawLactometerReading = 28.5m,
        temperatureCelsius = 29.0m,
        waterPercent = 0m,
        kqColour = "Blue",
        alcoholOutcomes = new Dictionary<string, string> { ["Alcohol80"] = "Negative" },
        verdict,
        failedParameter = verdict == "Reject" ? "Snf" : null,
        failedValue = verdict == "Reject" ? "7.10" : null
    };

    private static async Task<string> RegisterAsync(HttpClient client)
    {
        var societies = await client.GetFromJsonAsync<List<SocietyView>>("/api/societies", JsonOptions);

        var response = await client.PostAsJsonAsync("/api/consignments", new
        {
            societyId = societies!.First().Id,
            cans = new[] { new { canNumber = 1, quantityKg = 41.2m } }
        });

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<ConsignmentView>(JsonOptions))!.Reference;
    }

    private static async Task<List<string>> ReferencesAsync(HttpClient client, string query)
    {
        var page = await client.GetFromJsonAsync<JsonElement>($"/api/consignments{query}", JsonOptions);

        return page.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("reference").GetString()!)
            .ToList();
    }

    [Fact]
    public async Task Each_status_returns_only_the_consignments_in_it()
    {
        using var factory = new IntakeApiFactory();
        var client = factory.CreateClientAs(WonrichRoles.IntakeOfficer);

        var accepted = await RegisterAsync(client);
        var rejected = await RegisterAsync(client);
        var untested = await RegisterAsync(client);

        (await client.PostAsJsonAsync($"/api/consignments/{accepted}/quality-test", SoundPanel()))
            .EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync($"/api/consignments/{rejected}/quality-test", SoundPanel("Reject")))
            .EnsureSuccessStatusCode();

        Assert.Equal([accepted], await ReferencesAsync(client, "?status=Accepted"));
        Assert.Equal([rejected], await ReferencesAsync(client, "?status=Rejected"));
        Assert.Equal([untested], await ReferencesAsync(client, "?status=Registered"));
    }

    [Fact]
    public async Task Leaving_the_status_off_returns_every_consignment()
    {
        using var factory = new IntakeApiFactory();
        var client = factory.CreateClientAs(WonrichRoles.IntakeOfficer);

        await RegisterAsync(client);
        await RegisterAsync(client);

        var all = await client.GetFromJsonAsync<JsonElement>("/api/consignments", JsonOptions);

        Assert.Equal(2, all.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task The_status_narrows_the_total_and_not_just_the_page()
    {
        using var factory = new IntakeApiFactory();
        var client = factory.CreateClientAs(WonrichRoles.IntakeOfficer);

        var accepted = await RegisterAsync(client);
        await RegisterAsync(client);

        (await client.PostAsJsonAsync($"/api/consignments/{accepted}/quality-test", SoundPanel()))
            .EnsureSuccessStatusCode();

        var page = await client.GetFromJsonAsync<JsonElement>(
            "/api/consignments?status=Accepted", JsonOptions);

        Assert.Equal(1, page.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task A_status_that_is_not_a_lifecycle_state_is_refused()
    {
        using var factory = new IntakeApiFactory();
        var client = factory.CreateClientAs(WonrichRoles.IntakeOfficer);

        var response = await client.GetAsync("/api/consignments?status=Curdled");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task The_status_combines_with_the_other_filters()
    {
        using var factory = new IntakeApiFactory();
        var client = factory.CreateClientAs(WonrichRoles.IntakeOfficer);

        var accepted = await RegisterAsync(client);

        (await client.PostAsJsonAsync($"/api/consignments/{accepted}/quality-test", SoundPanel()))
            .EnsureSuccessStatusCode();

        var today = factory.Clock.LocalNow.ToString("yyyy-MM-dd");

        Assert.Equal([accepted], await ReferencesAsync(client, $"?status=Accepted&date={today}"));
        Assert.Empty(await ReferencesAsync(client, "?status=Accepted&date=2020-01-01"));
    }
}
