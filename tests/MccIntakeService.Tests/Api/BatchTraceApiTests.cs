using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using MccIntakeService.Application.Consignments;
using MccIntakeService.Application.Societies;
using MccIntakeService.Tests.Support;
using Wonrich.Auth.Authorization;

namespace MccIntakeService.Tests.Api;

/// <summary>Drives the SCRUM-12 batch trace endpoint over HTTP.</summary>
public class BatchTraceApiTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>Runs the whole chain and returns the batch reference.</summary>
    private static async Task<string> BatchAsync(IntakeApiFactory factory, params (string Tank, int Society)[] pours)
    {
        var officer = factory.CreateClientAs(WonrichRoles.IntakeOfficer);
        var societies = await officer.GetFromJsonAsync<List<SocietyView>>("/api/societies", JsonOptions);

        foreach (var (tank, societyIndex) in pours)
        {
            var registered = await officer.PostAsJsonAsync("/api/consignments", new
            {
                societyId = societies![societyIndex].Id,
                cans = new[] { new { canNumber = 1, quantityKg = 515m } }
            });

            registered.EnsureSuccessStatusCode();
            var reference = (await registered.Content.ReadFromJsonAsync<ConsignmentView>(JsonOptions))!.Reference;

            (await officer.PostAsJsonAsync($"/api/consignments/{reference}/quality-test", new
            {
                fatPercent = 4.5m,
                rawLactometerReading = 29.5m,
                temperatureCelsius = 29.0m,
                waterPercent = 0m,
                kqColour = "Blue",
                alcoholOutcomes = new Dictionary<string, string> { ["Alcohol80"] = "Negative" },
                verdict = "Accept"
            })).EnsureSuccessStatusCode();

            (await officer.PostAsJsonAsync($"/api/tanks/{tank}/pours", new
            {
                consignmentReference = reference
            })).EnsureSuccessStatusCode();
        }

        var manager = factory.CreateClientAs(WonrichRoles.MccManager);
        var note = await manager.PostAsJsonAsync("/api/dispatch-notes", new
        {
            bowserRegistration = "WP-CAB-1234",
            driverName = "Ranjith Fernando",
            draws = pours.Select(p => p.Tank).Distinct()
                .Select(tank => new { tankCode = tank, quantityLitres = 100m }).ToArray(),
            fatPercent = 4.0m,
            snf = 8.6m,
            kqColour = "Blue",
            stabilityGrade = "Stable",
            temperatureCelsius = 4.5m
        });

        note.EnsureSuccessStatusCode();
        var noteReference = (await note.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("reference").GetString();

        var intake = factory.CreateClientAs(WonrichRoles.FactoryIntakeOfficer);
        var screening = await intake.PostAsJsonAsync("/api/factory/arrivals", new
        {
            dispatchNoteReference = noteReference,
            smellPassed = true,
            colourPassed = true,
            temperaturePassed = true,
            temperatureCelsius = 4.8m
        });

        screening.EnsureSuccessStatusCode();

        return (await screening.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("batch").GetProperty("reference").GetString()!;
    }

    [Fact]
    public async Task A_batch_traces_back_to_its_tanks_and_consignments_over_http()
    {
        using var factory = new IntakeApiFactory();
        var batch = await BatchAsync(factory, ("T1", 0));

        var qco = factory.CreateClientAs(WonrichRoles.QualityAnalyst);
        var trace = await qco.GetFromJsonAsync<JsonElement>(
            $"/api/factory/batches/{batch}/trace", JsonOptions);

        Assert.Equal(batch, trace.GetProperty("batchReference").GetString());
        Assert.StartsWith("DN-", trace.GetProperty("dispatchNoteReference").GetString());

        var tank = trace.GetProperty("tanks").EnumerateArray().Single();
        Assert.Equal("T1", tank.GetProperty("tankCode").GetString());

        // The society list is ordered by code, so the pour above used whichever sorts first.
        var societies = await qco.GetFromJsonAsync<List<SocietyView>>("/api/societies", JsonOptions);

        var consignment = tank.GetProperty("consignments").EnumerateArray().Single();
        Assert.Equal(societies![0].Code, consignment.GetProperty("societyCode").GetString());
        Assert.NotEqual(JsonValueKind.Null, consignment.GetProperty("qualityTest").ValueKind);
    }

    [Fact]
    public async Task A_batch_drawing_from_several_tanks_resolves_all_of_them()
    {
        using var factory = new IntakeApiFactory();
        var batch = await BatchAsync(factory, ("T1", 0), ("T2", 1), ("T3", 2));

        var qco = factory.CreateClientAs(WonrichRoles.QualityAnalyst);
        var trace = await qco.GetFromJsonAsync<JsonElement>(
            $"/api/factory/batches/{batch}/trace", JsonOptions);

        Assert.Equal(3, trace.GetProperty("tanks").GetArrayLength());
        Assert.Equal(3, trace.GetProperty("societiesByMargin").GetArrayLength());
    }

    [Fact]
    public async Task Societies_come_back_ranked_by_margin()
    {
        using var factory = new IntakeApiFactory();
        var batch = await BatchAsync(factory, ("T1", 0), ("T1", 1));

        var qco = factory.CreateClientAs(WonrichRoles.QualityAnalyst);
        var trace = await qco.GetFromJsonAsync<JsonElement>(
            $"/api/factory/batches/{batch}/trace", JsonOptions);

        var margins = trace.GetProperty("societiesByMargin").EnumerateArray()
            .Select(society => society.GetProperty("tightestMargin").GetDecimal())
            .ToList();

        Assert.Equal(margins.OrderBy(margin => margin), margins);
    }

    [Fact]
    public async Task An_unknown_batch_returns_a_clear_404()
    {
        using var factory = new IntakeApiFactory();
        var qco = factory.CreateClientAs(WonrichRoles.QualityAnalyst);

        var response = await qco.GetAsync("/api/factory/batches/WR-20260823-99/trace");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("WR-20260823-99", problem.GetProperty("detail").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unauthenticated_caller_is_refused()
    {
        using var factory = new IntakeApiFactory();
        var batch = await BatchAsync(factory, ("T1", 0));

        var response = await factory.CreateClient().GetAsync($"/api/factory/batches/{batch}/trace");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(WonrichRoles.IntakeOfficer)]
    [InlineData(WonrichRoles.MccManager)]
    [InlineData(WonrichRoles.BowserOperator)]
    public async Task A_role_outside_the_trace_policy_is_refused_with_403(string role)
    {
        using var factory = new IntakeApiFactory();
        var batch = await BatchAsync(factory, ("T1", 0));

        var response = await factory.CreateClientAs(role).GetAsync($"/api/factory/batches/{batch}/trace");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task The_trace_endpoint_is_documented_in_swagger()
    {
        using var factory = new IntakeApiFactory();

        var document = await factory.CreateClient().GetStringAsync("/swagger/v1/swagger.json");

        Assert.Contains("/api/factory/batches/{reference}/trace", document, StringComparison.Ordinal);
    }
}
