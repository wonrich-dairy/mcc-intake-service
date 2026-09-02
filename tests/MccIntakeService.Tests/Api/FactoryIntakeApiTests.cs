using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using MccIntakeService.Application.Consignments;
using MccIntakeService.Application.Societies;
using MccIntakeService.Tests.Support;
using Wonrich.Auth.Authorization;

namespace MccIntakeService.Tests.Api;

/// <summary>Drives the SCRUM-9 factory intake endpoints over HTTP.</summary>
public class FactoryIntakeApiTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>Runs a consignment through gate, tank and dispatch, returning the note reference.</summary>
    private static async Task<string> DispatchNoteAsync(
        IntakeApiFactory factory,
        string tankCode = "T1",
        int societyIndex = 0)
    {
        var officer = factory.CreateClientAs(WonrichRoles.IntakeOfficer);

        var societies = await officer.GetFromJsonAsync<List<SocietyView>>("/api/societies", JsonOptions);

        var registered = await officer.PostAsJsonAsync("/api/consignments", new
        {
            societyId = societies![societyIndex].Id,
            cans = new[] { new { canNumber = 1, quantityKg = 515m } }
        });

        registered.EnsureSuccessStatusCode();
        var reference = (await registered.Content.ReadFromJsonAsync<ConsignmentView>(JsonOptions))!.Reference;

        (await officer.PostAsJsonAsync($"/api/consignments/{reference}/quality-test", new
        {
            fatPercent = 4.1m,
            rawLactometerReading = 28.5m,
            temperatureCelsius = 29.0m,
            waterPercent = 0m,
            kqColour = "Blue",
            alcoholOutcomes = new Dictionary<string, string> { ["Alcohol80"] = "Negative" },
            verdict = "Accept"
        })).EnsureSuccessStatusCode();

        (await officer.PostAsJsonAsync($"/api/tanks/{tankCode}/pours", new
        {
            consignmentReference = reference
        })).EnsureSuccessStatusCode();

        var manager = factory.CreateClientAs(WonrichRoles.MccManager);
        var note = await manager.PostAsJsonAsync("/api/dispatch-notes", new
        {
            bowserRegistration = "WP-CAB-1234",
            driverName = "Ranjith Fernando",
            draws = new[] { new { tankCode, quantityLitres = 100m } },
            fatPercent = 4.0m,
            snf = 8.6m,
            kqColour = "Blue",
            stabilityGrade = "Stable",
            temperatureCelsius = 4.5m
        });

        note.EnsureSuccessStatusCode();

        return (await note.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("reference").GetString()!;
    }

    private static object Screening(
        string dispatchNote,
        bool smell = true,
        bool colour = true,
        bool temperature = true) => new
        {
            dispatchNoteReference = dispatchNote,
            smellPassed = smell,
            colourPassed = colour,
            temperaturePassed = temperature,
            temperatureCelsius = 4.8m
        };

    [Fact]
    public async Task An_arrival_in_the_future_returns_400()
    {
        using var factory = new IntakeApiFactory();
        var note = await DispatchNoteAsync(factory);
        var client = factory.CreateClientAs(WonrichRoles.FactoryIntakeOfficer);

        var response = await client.PostAsJsonAsync("/api/factory/arrivals", new
        {
            dispatchNoteReference = note,
            smellPassed = true,
            colourPassed = true,
            temperaturePassed = true,
            temperatureCelsius = 4.8m,
            arrivedAtLocal = "2030-01-01T08:00:00"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task A_clean_screening_returns_201_with_the_new_batch()
    {
        using var factory = new IntakeApiFactory();
        var note = await DispatchNoteAsync(factory);
        var client = factory.CreateClientAs(WonrichRoles.FactoryIntakeOfficer);

        var response = await client.PostAsJsonAsync("/api/factory/arrivals", Screening(note));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var screening = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Accepted", screening.GetProperty("outcome").GetString());

        var reference = screening.GetProperty("batch").GetProperty("reference").GetString();
        Assert.StartsWith("WR-", reference);

        var batch = await client.GetFromJsonAsync<JsonElement>($"/api/factory/batches/{reference}", JsonOptions);
        Assert.Equal(note, batch.GetProperty("dispatchNoteReference").GetString());
    }

    [Fact]
    public async Task A_failed_screening_creates_no_batch_and_names_the_parameter()
    {
        using var factory = new IntakeApiFactory();
        var note = await DispatchNoteAsync(factory);
        var client = factory.CreateClientAs(WonrichRoles.FactoryIntakeOfficer);

        var response = await client.PostAsJsonAsync("/api/factory/arrivals", Screening(note, smell: false));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var screening = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Rejected", screening.GetProperty("outcome").GetString());
        Assert.Equal("Smell", screening.GetProperty("failedParameters").GetString());
        Assert.Equal(JsonValueKind.Null, screening.GetProperty("batch").ValueKind);

        Assert.Empty(await client.GetFromJsonAsync<List<JsonElement>>("/api/factory/batches", JsonOptions) ?? []);
    }

    [Fact]
    public async Task Screening_the_same_note_twice_returns_409()
    {
        using var factory = new IntakeApiFactory();
        var note = await DispatchNoteAsync(factory);
        var client = factory.CreateClientAs(WonrichRoles.FactoryIntakeOfficer);

        await client.PostAsJsonAsync("/api/factory/arrivals", Screening(note));
        var again = await client.PostAsJsonAsync("/api/factory/arrivals", Screening(note));

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("application/problem+json", again.Content.Headers.ContentType?.MediaType);

        // The route publishes IntakeProblemDetails for 409, so the body owes a code to branch on
        // just as the 404 on this route does.
        var problem = await again.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrEmpty(problem.GetProperty("code").GetString()));
        Assert.Contains("wonrich.dev", problem.GetProperty("type").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unknown_dispatch_note_returns_404()
    {
        using var factory = new IntakeApiFactory();
        var client = factory.CreateClientAs(WonrichRoles.FactoryIntakeOfficer);

        var response = await client.PostAsJsonAsync("/api/factory/arrivals", Screening("DN-20260823-99"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        // The route publishes IntakeProblemDetails for this status, so the body has to carry the
        // code a caller branches on, not just the status.
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("entity_not_found", problem.GetProperty("code").GetString());
        Assert.Contains("wonrich.dev", problem.GetProperty("type").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_screening_without_a_dispatch_note_returns_400()
    {
        using var factory = new IntakeApiFactory();
        var client = factory.CreateClientAs(WonrichRoles.FactoryIntakeOfficer);

        var response = await client.PostAsJsonAsync("/api/factory/arrivals", Screening(""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Batches_can_be_queried_by_date_and_dispatch_note()
    {
        using var factory = new IntakeApiFactory();
        var note = await DispatchNoteAsync(factory);
        var client = factory.CreateClientAs(WonrichRoles.FactoryIntakeOfficer);

        await client.PostAsJsonAsync("/api/factory/arrivals", Screening(note));

        var day = DateOnly.FromDateTime(factory.Clock.LocalNow);

        Assert.Single((await client.GetFromJsonAsync<List<JsonElement>>(
            $"/api/factory/batches?date={day:yyyy-MM-dd}", JsonOptions))!);
        Assert.Empty((await client.GetFromJsonAsync<List<JsonElement>>(
            $"/api/factory/batches?date={day.AddDays(1):yyyy-MM-dd}", JsonOptions))!);
        Assert.Single((await client.GetFromJsonAsync<List<JsonElement>>(
            $"/api/factory/batches?dispatchNote={note}", JsonOptions))!);
    }

    [Fact]
    public async Task An_unknown_batch_reference_returns_404()
    {
        using var factory = new IntakeApiFactory();
        var client = factory.CreateClientAs(WonrichRoles.FactoryIntakeOfficer);

        var response = await client.GetAsync("/api/factory/batches/WR-20260823-99");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task An_unauthenticated_caller_is_refused()
    {
        using var factory = new IntakeApiFactory();

        var response = await factory.CreateClient().GetAsync("/api/factory/batches");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_role_with_no_factory_duties_is_refused_with_403()
    {
        using var factory = new IntakeApiFactory();

        var response = await factory.CreateClientAs(WonrichRoles.IntakeOfficer)
            .GetAsync("/api/factory/batches");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task The_factory_endpoints_are_documented_in_swagger()
    {
        using var factory = new IntakeApiFactory();

        var document = await factory.CreateClient().GetStringAsync("/swagger/v1/swagger.json");

        Assert.Contains("/api/factory/arrivals", document, StringComparison.Ordinal);
        Assert.Contains("/api/factory/batches", document, StringComparison.Ordinal);
        Assert.Contains("/api/factory/batches/{reference}", document, StringComparison.Ordinal);
    }
}
