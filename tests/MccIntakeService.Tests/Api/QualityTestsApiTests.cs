using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using MccIntakeService.Application.Consignments;
using MccIntakeService.Application.Societies;
using MccIntakeService.Tests.Support;
using Wonrich.Auth.Authorization;

namespace MccIntakeService.Tests.Api;

/// <summary>Drives the SCRUM-7 gate testing endpoints over HTTP.</summary>
public class QualityTestsApiTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private static async Task<string> RegisterConsignmentAsync(HttpClient client)
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

    private static object SoundPanel(
        string verdict = "Accept",
        decimal fat = 4.1m,
        string kq = "Blue",
        string? failedParameter = null,
        string? failedValue = null) => new
        {
            fatPercent = fat,
            rawLactometerReading = 28.5m,
            temperatureCelsius = 29.0m,
            waterPercent = 0m,
            kqColour = kq,
            alcoholOutcomes = new Dictionary<string, string> { ["Alcohol80"] = "Negative" },
            verdict,
            failedParameter,
            failedValue
        };

    [Fact]
    public async Task The_preview_returns_the_derived_values_without_recording()
    {
        using var factory = new IntakeApiFactory();
        var client = factory.CreateClientAs(WonrichRoles.IntakeOfficer);
        var reference = await RegisterConsignmentAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/api/consignments/{reference}/quality-test/preview", SoundPanel());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var preview = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(28.90m, preview.GetProperty("correctedClr").GetDecimal());
        Assert.Equal(8.85m, preview.GetProperty("snf").GetDecimal());
        Assert.Equal(12.95m, preview.GetProperty("totalSolids").GetDecimal());
        Assert.True(preview.GetProperty("meetsStandard").GetBoolean());

        // Nothing recorded: reading it back is still a 404.
        var read = await client.GetAsync($"/api/consignments/{reference}/quality-test");
        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
    }

    [Fact]
    public async Task Recording_a_panel_returns_201_and_is_then_readable()
    {
        using var factory = new IntakeApiFactory();
        var client = factory.CreateClientAs(WonrichRoles.IntakeOfficer);
        var reference = await RegisterConsignmentAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/api/consignments/{reference}/quality-test", SoundPanel());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var read = await client.GetFromJsonAsync<JsonElement>(
            $"/api/consignments/{reference}/quality-test", JsonOptions);

        Assert.Equal("Accept", read.GetProperty("verdict").GetString());
        Assert.Equal(reference, read.GetProperty("consignmentReference").GetString());
    }

    [Fact]
    public async Task Testing_the_same_consignment_twice_returns_409()
    {
        using var factory = new IntakeApiFactory();
        var client = factory.CreateClientAs(WonrichRoles.IntakeOfficer);
        var reference = await RegisterConsignmentAsync(client);

        await client.PostAsJsonAsync($"/api/consignments/{reference}/quality-test", SoundPanel());
        var again = await client.PostAsJsonAsync($"/api/consignments/{reference}/quality-test", SoundPanel());

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("application/problem+json", again.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Testing_a_consignment_that_does_not_exist_returns_404()
    {
        using var factory = new IntakeApiFactory();
        var client = factory.CreateClientAs(WonrichRoles.IntakeOfficer);

        var response = await client.PostAsJsonAsync(
            "/api/consignments/MCC-20260823-XX-99/quality-test", SoundPanel());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_rejection_without_a_reason_returns_400()
    {
        using var factory = new IntakeApiFactory();
        var client = factory.CreateClientAs(WonrichRoles.IntakeOfficer);
        var reference = await RegisterConsignmentAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/api/consignments/{reference}/quality-test", SoundPanel(verdict: "Reject"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_rejection_naming_its_reason_is_accepted()
    {
        using var factory = new IntakeApiFactory();
        var client = factory.CreateClientAs(WonrichRoles.IntakeOfficer);
        var reference = await RegisterConsignmentAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/api/consignments/{reference}/quality-test",
            SoundPanel(verdict: "Reject", fat: 1.0m, failedParameter: "FatPercent", failedValue: "1.00"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task A_clot_on_boiling_cannot_be_accepted_over_http()
    {
        using var factory = new IntakeApiFactory();
        var client = factory.CreateClientAs(WonrichRoles.IntakeOfficer);
        var reference = await RegisterConsignmentAsync(client);

        var response = await client.PostAsJsonAsync($"/api/consignments/{reference}/quality-test", new
        {
            fatPercent = 4.1m,
            rawLactometerReading = 28.5m,
            temperatureCelsius = 29.0m,
            waterPercent = 0m,
            kqColour = "Blue",
            alcoholOutcomes = new Dictionary<string, string>
            {
                ["Alcohol80"] = "Positive",
                ["Alcohol75"] = "Positive",
                ["Alcohol68"] = "Positive",
                ["ClotOnBoiling"] = "Positive"
            },
            verdict = "Accept"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task An_unauthenticated_caller_is_refused()
    {
        using var factory = new IntakeApiFactory();
        var officer = factory.CreateClientAs(WonrichRoles.IntakeOfficer);
        var reference = await RegisterConsignmentAsync(officer);

        var response = await factory.CreateClient()
            .PostAsJsonAsync($"/api/consignments/{reference}/quality-test", SoundPanel());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_role_with_no_gate_duties_is_refused_with_403()
    {
        using var factory = new IntakeApiFactory();
        var officer = factory.CreateClientAs(WonrichRoles.IntakeOfficer);
        var reference = await RegisterConsignmentAsync(officer);

        var response = await factory.CreateClientAs(WonrichRoles.ProductionManager)
            .PostAsJsonAsync($"/api/consignments/{reference}/quality-test", SoundPanel());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task A_quality_analyst_may_record_a_panel()
    {
        using var factory = new IntakeApiFactory();
        var officer = factory.CreateClientAs(WonrichRoles.IntakeOfficer);
        var reference = await RegisterConsignmentAsync(officer);

        var response = await factory.CreateClientAs(WonrichRoles.QualityAnalyst)
            .PostAsJsonAsync($"/api/consignments/{reference}/quality-test", SoundPanel());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task An_out_of_range_reading_is_rejected_by_model_validation()
    {
        using var factory = new IntakeApiFactory();
        var client = factory.CreateClientAs(WonrichRoles.IntakeOfficer);
        var reference = await RegisterConsignmentAsync(client);

        var response = await client.PostAsJsonAsync($"/api/consignments/{reference}/quality-test", new
        {
            fatPercent = 99m,
            rawLactometerReading = 28.5m,
            temperatureCelsius = 29.0m,
            waterPercent = 0m,
            kqColour = "Blue",
            alcoholOutcomes = new Dictionary<string, string> { ["Alcohol80"] = "Negative" },
            verdict = "Accept"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task The_gate_testing_endpoints_are_documented_in_swagger()
    {
        using var factory = new IntakeApiFactory();

        var document = await factory.CreateClient().GetStringAsync("/swagger/v1/swagger.json");

        Assert.Contains("/api/consignments/{reference}/quality-test", document, StringComparison.Ordinal);
        Assert.Contains("/api/consignments/{reference}/quality-test/preview", document, StringComparison.Ordinal);
    }
}
