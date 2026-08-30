using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using MccIntakeService.Application.Consignments;
using MccIntakeService.Application.Societies;
using MccIntakeService.Tests.Support;
using Wonrich.Auth.Authorization;

namespace MccIntakeService.Tests.Api;

/// <summary>Drives the SCRUM-52 tank and manifest endpoints over HTTP.</summary>
public class TanksApiTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>Registers a consignment and takes it through the gate to the given verdict.</summary>
    private static async Task<string> ConsignmentAsync(
        HttpClient client,
        string? verdict = "Accept",
        int societyIndex = 0)
    {
        var societies = await client.GetFromJsonAsync<List<SocietyView>>("/api/societies", JsonOptions);

        var registered = await client.PostAsJsonAsync("/api/consignments", new
        {
            societyId = societies![societyIndex].Id,
            cans = new[] { new { canNumber = 1, quantityKg = 41.2m } }
        });

        registered.EnsureSuccessStatusCode();
        var reference = (await registered.Content.ReadFromJsonAsync<ConsignmentView>(JsonOptions))!.Reference;

        if (verdict is null)
        {
            return reference;
        }

        var test = await client.PostAsJsonAsync($"/api/consignments/{reference}/quality-test", new
        {
            fatPercent = 4.1m,
            rawLactometerReading = 28.5m,
            temperatureCelsius = 29.0m,
            waterPercent = 0m,
            kqColour = "Blue",
            alcoholOutcomes = new Dictionary<string, string> { ["Alcohol80"] = "Negative" },
            verdict,
            failedParameter = verdict == "Reject" ? "FatPercent" : null,
            failedValue = verdict == "Reject" ? "4.10" : null
        });

        test.EnsureSuccessStatusCode();

        return reference;
    }

    [Fact]
    public async Task The_three_tanks_are_listed_with_their_totals()
    {
        using var factory = new IntakeApiFactory();
        var client = factory.CreateClientAs(WonrichRoles.IntakeOfficer);

        var tanks = await client.GetFromJsonAsync<List<JsonElement>>("/api/tanks", JsonOptions);

        Assert.Equal(3, tanks!.Count);
        Assert.Equal(["T1", "T2", "T3"], tanks.Select(t => t.GetProperty("code").GetString()));
    }

    [Fact]
    public async Task Pouring_returns_201_and_the_updated_manifest()
    {
        using var factory = new IntakeApiFactory();
        var client = factory.CreateClientAs(WonrichRoles.IntakeOfficer);
        var reference = await ConsignmentAsync(client);

        var response = await client.PostAsJsonAsync(
            "/api/tanks/T1/pours", new { consignmentReference = reference });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var manifest = await response.Content.ReadFromJsonAsync<JsonElement>();
        var entry = Assert.Single(manifest.GetProperty("entries").EnumerateArray().ToList());

        Assert.Equal(reference, entry.GetProperty("consignmentReference").GetString());
        Assert.Equal(1, manifest.GetProperty("tank").GetProperty("consignmentCount").GetInt32());
    }

    [Fact]
    public async Task Only_accepted_unpoured_consignments_are_offered()
    {
        using var factory = new IntakeApiFactory();
        var client = factory.CreateClientAs(WonrichRoles.IntakeOfficer);

        var accepted = await ConsignmentAsync(client);
        await ConsignmentAsync(client, "Reject", societyIndex: 1);
        await ConsignmentAsync(client, null, societyIndex: 2);

        var pourable = await client.GetFromJsonAsync<List<JsonElement>>("/api/tanks/pourable", JsonOptions);

        Assert.Equal(accepted, Assert.Single(pourable!).GetProperty("reference").GetString());
    }

    [Fact]
    public async Task Pouring_an_untested_consignment_returns_400()
    {
        using var factory = new IntakeApiFactory();
        var client = factory.CreateClientAs(WonrichRoles.IntakeOfficer);
        var reference = await ConsignmentAsync(client, verdict: null);

        var response = await client.PostAsJsonAsync(
            "/api/tanks/T1/pours", new { consignmentReference = reference });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Pouring_a_rejected_consignment_returns_400()
    {
        using var factory = new IntakeApiFactory();
        var client = factory.CreateClientAs(WonrichRoles.IntakeOfficer);
        var reference = await ConsignmentAsync(client, "Reject");

        var response = await client.PostAsJsonAsync(
            "/api/tanks/T1/pours", new { consignmentReference = reference });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Pouring_the_same_consignment_twice_returns_409()
    {
        using var factory = new IntakeApiFactory();
        var client = factory.CreateClientAs(WonrichRoles.IntakeOfficer);
        var reference = await ConsignmentAsync(client);

        await client.PostAsJsonAsync("/api/tanks/T1/pours", new { consignmentReference = reference });
        var again = await client.PostAsJsonAsync("/api/tanks/T2/pours", new { consignmentReference = reference });

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("application/problem+json", again.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task An_unknown_tank_returns_404()
    {
        using var factory = new IntakeApiFactory();
        var client = factory.CreateClientAs(WonrichRoles.IntakeOfficer);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/tanks/T9/manifest")).StatusCode);

        var reference = await ConsignmentAsync(client);
        var pour = await client.PostAsJsonAsync(
            "/api/tanks/T9/pours", new { consignmentReference = reference });

        Assert.Equal(HttpStatusCode.NotFound, pour.StatusCode);
    }

    [Fact]
    public async Task A_pour_without_a_consignment_reference_returns_400()
    {
        using var factory = new IntakeApiFactory();
        var client = factory.CreateClientAs(WonrichRoles.IntakeOfficer);

        var response = await client.PostAsJsonAsync("/api/tanks/T1/pours", new { consignmentReference = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task The_manifest_can_be_queried_by_tank_and_date()
    {
        using var factory = new IntakeApiFactory();
        var client = factory.CreateClientAs(WonrichRoles.IntakeOfficer);
        var reference = await ConsignmentAsync(client);

        await client.PostAsJsonAsync("/api/tanks/T1/pours", new { consignmentReference = reference });

        var day = DateOnly.FromDateTime(factory.Clock.LocalNow);

        var onDay = await client.GetFromJsonAsync<JsonElement>(
            $"/api/tanks/T1/manifest?date={day:yyyy-MM-dd}", JsonOptions);
        var otherDay = await client.GetFromJsonAsync<JsonElement>(
            $"/api/tanks/T1/manifest?date={day.AddDays(1):yyyy-MM-dd}", JsonOptions);

        Assert.Single(onDay.GetProperty("entries").EnumerateArray().ToList());
        Assert.Empty(otherDay.GetProperty("entries").EnumerateArray().ToList());
    }

    /// <summary>
    /// Between midnight and 05:30 the centre's day and the UTC day disagree. The cutoff, the gate
    /// reference and every other date at the centre run on local time; filing a pour by UTC put
    /// the small hours under the previous day, and AC7's by-date query returned the wrong one.
    /// </summary>
    [Fact]
    public async Task A_pour_in_the_small_hours_is_filed_under_the_centre_s_day()
    {
        using var factory = new IntakeApiFactory();
        var client = factory.CreateClientAs(WonrichRoles.IntakeOfficer);
        var reference = await ConsignmentAsync(client);

        // 02:00 in Colombo is 20:30 the previous day in UTC.
        factory.Clock.LocalNow = new DateTime(2026, 8, 24, 2, 0, 0);

        await client.PostAsJsonAsync("/api/tanks/T1/pours", new { consignmentReference = reference });

        var centreDay = await client.GetFromJsonAsync<JsonElement>(
            "/api/tanks/T1/manifest?date=2026-08-24", JsonOptions);
        var utcDay = await client.GetFromJsonAsync<JsonElement>(
            "/api/tanks/T1/manifest?date=2026-08-23", JsonOptions);

        Assert.Single(centreDay.GetProperty("entries").EnumerateArray().ToList());
        Assert.Empty(utcDay.GetProperty("entries").EnumerateArray().ToList());
    }

    /// <summary>
    /// `code` is the field a consumer branches on, and all four refusals from these two routes are
    /// documented as IntakeProblemDetails. Built with ControllerBase.Problem(...) the 404s and the
    /// 409 carried no code at all, so a client that handled one refusal broke on the others.
    /// </summary>
    [Fact]
    public async Task Every_refusal_carries_the_code_the_contract_documents()
    {
        using var factory = new IntakeApiFactory();
        var client = factory.CreateClientAs(WonrichRoles.IntakeOfficer);
        var reference = await ConsignmentAsync(client);

        var unknownTank = await client.PostAsJsonAsync(
            "/api/tanks/T9/pours", new { consignmentReference = reference });

        await client.PostAsJsonAsync("/api/tanks/T1/pours", new { consignmentReference = reference });
        var alreadyPoured = await client.PostAsJsonAsync(
            "/api/tanks/T2/pours", new { consignmentReference = reference });

        var untested = await ConsignmentAsync(client, verdict: null, societyIndex: 1);
        var notTested = await client.PostAsJsonAsync(
            "/api/tanks/T1/pours", new { consignmentReference = untested });

        var unknownManifest = await client.GetAsync("/api/tanks/T9/manifest");

        Assert.Equal("entity_not_found", await CodeOf(unknownTank));
        Assert.Equal("consignment_already_poured", await CodeOf(alreadyPoured));
        Assert.Equal("domain_validation_failed", await CodeOf(notTested));
        Assert.Equal("entity_not_found", await CodeOf(unknownManifest));
    }

    private static async Task<string?> CodeOf(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions))
            .GetProperty("code")
            .GetString();

    [Fact]
    public async Task An_unauthenticated_caller_is_refused()
    {
        using var factory = new IntakeApiFactory();

        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/tanks")).StatusCode);
    }

    [Fact]
    public async Task A_role_with_no_gate_duties_is_refused_with_403()
    {
        using var factory = new IntakeApiFactory();

        var response = await factory.CreateClientAs(WonrichRoles.ProductionManager).GetAsync("/api/tanks");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task The_tank_endpoints_are_documented_in_swagger()
    {
        using var factory = new IntakeApiFactory();

        var document = await factory.CreateClient().GetStringAsync("/swagger/v1/swagger.json");

        Assert.Contains("/api/tanks/{code}/pours", document, StringComparison.Ordinal);
        Assert.Contains("/api/tanks/{code}/manifest", document, StringComparison.Ordinal);
        Assert.Contains("/api/tanks/pourable", document, StringComparison.Ordinal);
    }
}
