using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using MccIntakeService.Application.Consignments;
using MccIntakeService.Application.Societies;
using MccIntakeService.Tests.Support;
using Wonrich.Auth.Authorization;

namespace MccIntakeService.Tests.Api;

/// <summary>Drives the SCRUM-8 dispatch note endpoints over HTTP.</summary>
public class DispatchNotesApiTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>Takes a consignment through the gate and pours it into the given tank.</summary>
    private static async Task PourAsync(HttpClient client, string tankCode, int societyIndex)
    {
        var societies = await client.GetFromJsonAsync<List<SocietyView>>("/api/societies", JsonOptions);

        var registered = await client.PostAsJsonAsync("/api/consignments", new
        {
            societyId = societies![societyIndex].Id,
            cans = new[] { new { canNumber = 1, quantityKg = 515m } }
        });

        registered.EnsureSuccessStatusCode();
        var reference = (await registered.Content.ReadFromJsonAsync<ConsignmentView>(JsonOptions))!.Reference;

        (await client.PostAsJsonAsync($"/api/consignments/{reference}/quality-test", new
        {
            fatPercent = 4.1m,
            rawLactometerReading = 28.5m,
            temperatureCelsius = 29.0m,
            waterPercent = 0m,
            kqColour = "Blue",
            alcoholOutcomes = new Dictionary<string, string> { ["Alcohol80"] = "Negative" },
            verdict = "Accept"
        })).EnsureSuccessStatusCode();

        (await client.PostAsJsonAsync($"/api/tanks/{tankCode}/pours", new
        {
            consignmentReference = reference
        })).EnsureSuccessStatusCode();
    }

    private static object Note(params (string Tank, decimal Litres)[] draws) => new
    {
        bowserRegistration = "WP-CAB-1234",
        driverName = "Ranjith Fernando",
        draws = draws.Select(draw => new { tankCode = draw.Tank, quantityLitres = draw.Litres }).ToArray(),
        fatPercent = 4.0m,
        snf = 8.6m,
        kqColour = "Blue",
        stabilityGrade = "Stable",
        temperatureCelsius = 4.5m,
        remarks = "Morning load"
    };

    [Fact]
    public async Task Recording_a_note_returns_201_and_it_is_then_fetchable()
    {
        using var factory = new IntakeApiFactory();
        var officer = factory.CreateClientAs(WonrichRoles.IntakeOfficer);
        await PourAsync(officer, "T1", 0);

        var manager = factory.CreateClientAs(WonrichRoles.MccManager);
        var response = await manager.PostAsJsonAsync("/api/dispatch-notes", Note(("T1", 100m)));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var created = await response.Content.ReadFromJsonAsync<JsonElement>();
        var reference = created.GetProperty("reference").GetString();

        Assert.StartsWith("DN-", reference);

        var fetched = await manager.GetFromJsonAsync<JsonElement>(
            $"/api/dispatch-notes/{reference}", JsonOptions);

        Assert.Equal("WP-CAB-1234", fetched.GetProperty("bowserRegistration").GetString());
        Assert.Equal(100m, fetched.GetProperty("totalQuantityLitres").GetDecimal());
    }

    [Fact]
    public async Task The_note_resolves_to_its_contributing_consignments()
    {
        using var factory = new IntakeApiFactory();
        var officer = factory.CreateClientAs(WonrichRoles.IntakeOfficer);
        await PourAsync(officer, "T1", 0);
        await PourAsync(officer, "T1", 1);

        var manager = factory.CreateClientAs(WonrichRoles.MccManager);
        var response = await manager.PostAsJsonAsync("/api/dispatch-notes", Note(("T1", 100m)));

        var note = await response.Content.ReadFromJsonAsync<JsonElement>();
        var source = note.GetProperty("sources").EnumerateArray().Single();

        Assert.Equal(2, source.GetProperty("contributingConsignments").GetArrayLength());
    }

    [Fact]
    public async Task Drawing_more_than_a_tank_holds_returns_400()
    {
        using var factory = new IntakeApiFactory();
        var officer = factory.CreateClientAs(WonrichRoles.IntakeOfficer);
        await PourAsync(officer, "T1", 0);

        var manager = factory.CreateClientAs(WonrichRoles.MccManager);
        var response = await manager.PostAsJsonAsync("/api/dispatch-notes", Note(("T1", 99_000m)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Submitting_a_note_closes_the_tank_to_further_pours()
    {
        using var factory = new IntakeApiFactory();
        var officer = factory.CreateClientAs(WonrichRoles.IntakeOfficer);
        await PourAsync(officer, "T1", 0);

        var manager = factory.CreateClientAs(WonrichRoles.MccManager);
        (await manager.PostAsJsonAsync("/api/dispatch-notes", Note(("T1", 100m)))).EnsureSuccessStatusCode();

        // Register and accept another consignment, then try to pour it into the closed tank.
        var societies = await officer.GetFromJsonAsync<List<SocietyView>>("/api/societies", JsonOptions);
        var registered = await officer.PostAsJsonAsync("/api/consignments", new
        {
            societyId = societies![1].Id,
            cans = new[] { new { canNumber = 1, quantityKg = 41.2m } }
        });
        var reference = (await registered.Content.ReadFromJsonAsync<ConsignmentView>(JsonOptions))!.Reference;

        await officer.PostAsJsonAsync($"/api/consignments/{reference}/quality-test", new
        {
            fatPercent = 4.1m,
            rawLactometerReading = 28.5m,
            temperatureCelsius = 29.0m,
            waterPercent = 0m,
            kqColour = "Blue",
            alcoholOutcomes = new Dictionary<string, string> { ["Alcohol80"] = "Negative" },
            verdict = "Accept"
        });

        var pour = await officer.PostAsJsonAsync("/api/tanks/T1/pours", new { consignmentReference = reference });

        Assert.Equal(HttpStatusCode.BadRequest, pour.StatusCode);
    }

    [Fact]
    public async Task An_unknown_tank_code_returns_404()
    {
        using var factory = new IntakeApiFactory();
        var manager = factory.CreateClientAs(WonrichRoles.MccManager);

        var response = await manager.PostAsJsonAsync("/api/dispatch-notes", Note(("T9", 10m)));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task An_unknown_reference_returns_404()
    {
        using var factory = new IntakeApiFactory();
        var manager = factory.CreateClientAs(WonrichRoles.MccManager);

        var response = await manager.GetAsync("/api/dispatch-notes/DN-20260823-99");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_note_without_a_bowser_or_driver_returns_400()
    {
        using var factory = new IntakeApiFactory();
        var manager = factory.CreateClientAs(WonrichRoles.MccManager);

        var response = await manager.PostAsJsonAsync("/api/dispatch-notes", new
        {
            bowserRegistration = "",
            driverName = "",
            draws = new[] { new { tankCode = "T1", quantityLitres = 10m } },
            fatPercent = 4.0m,
            snf = 8.6m,
            kqColour = "Blue",
            stabilityGrade = "Stable",
            temperatureCelsius = 4.5m
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_note_with_no_source_tanks_returns_400()
    {
        using var factory = new IntakeApiFactory();
        var manager = factory.CreateClientAs(WonrichRoles.MccManager);

        var response = await manager.PostAsJsonAsync("/api/dispatch-notes", Note());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Notes_can_be_listed_and_filtered_by_date()
    {
        using var factory = new IntakeApiFactory();
        var officer = factory.CreateClientAs(WonrichRoles.IntakeOfficer);
        await PourAsync(officer, "T1", 0);

        var manager = factory.CreateClientAs(WonrichRoles.MccManager);
        await manager.PostAsJsonAsync("/api/dispatch-notes", Note(("T1", 100m)));

        var day = DateOnly.FromDateTime(factory.Clock.LocalNow);

        var onDay = await manager.GetFromJsonAsync<List<JsonElement>>(
            $"/api/dispatch-notes?date={day:yyyy-MM-dd}", JsonOptions);
        var otherDay = await manager.GetFromJsonAsync<List<JsonElement>>(
            $"/api/dispatch-notes?date={day.AddDays(1):yyyy-MM-dd}", JsonOptions);

        Assert.Single(onDay!);
        Assert.Empty(otherDay!);
    }

    [Fact]
    public async Task An_unauthenticated_caller_is_refused()
    {
        using var factory = new IntakeApiFactory();

        var response = await factory.CreateClient().GetAsync("/api/dispatch-notes");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_role_with_no_dispatch_duties_is_refused_with_403()
    {
        using var factory = new IntakeApiFactory();

        var response = await factory.CreateClientAs(WonrichRoles.QualityAnalyst)
            .GetAsync("/api/dispatch-notes");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// Both 404s are documented as IntakeProblemDetails, whose `code` is the field a consumer
    /// branches on. Built with ControllerBase.Problem(...) they carried no code at all.
    /// </summary>
    [Fact]
    public async Task Both_not_found_refusals_carry_the_code_the_contract_documents()
    {
        using var factory = new IntakeApiFactory();
        var officer = factory.CreateClientAs(WonrichRoles.IntakeOfficer);
        await PourAsync(officer, "T1", 0);

        var manager = factory.CreateClientAs(WonrichRoles.MccManager);

        var unknownNote = await manager.GetAsync("/api/dispatch-notes/DN-20260823-99");
        var unknownTank = await manager.PostAsJsonAsync("/api/dispatch-notes", Note(("T9", 10m)));

        Assert.Equal(HttpStatusCode.NotFound, unknownNote.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknownTank.StatusCode);
        Assert.Equal("entity_not_found", await CodeOf(unknownNote));
        Assert.Equal("entity_not_found", await CodeOf(unknownTank));
    }

    private static async Task<string?> CodeOf(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions))
            .GetProperty("code")
            .GetString();

    [Fact]
    public async Task The_dispatch_endpoints_are_documented_in_swagger()
    {
        using var factory = new IntakeApiFactory();

        var document = await factory.CreateClient().GetStringAsync("/swagger/v1/swagger.json");

        Assert.Contains("/api/dispatch-notes", document, StringComparison.Ordinal);
        Assert.Contains("/api/dispatch-notes/{reference}", document, StringComparison.Ordinal);
    }
}
