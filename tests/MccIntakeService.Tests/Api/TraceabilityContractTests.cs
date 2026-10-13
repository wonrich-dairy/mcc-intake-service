using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MccIntakeService.Application.Societies;
using MccIntakeService.Tests.Support;
using Wonrich.Auth.Authorization;

namespace MccIntakeService.Tests.Api;

/// <summary>
/// What the Traceability service depends on (SCRUM-132). It copied BatchTraceView, TracedTankView,
/// TracedConsignmentView, TracedQualityTestView, MeasureMargin and BatchView at c83508e and caches
/// each trace as sent.
/// </summary>
/// <remarks>
/// A failure here is not necessarily a bug, but it is a change Traceability has to hear about before
/// it ships. Update the expectation and tell them in the same PR.
/// </remarks>
public class TraceabilityContractTests
{
    private static readonly string[] TraceFields =
    [
        "batchReference", "batchDate", "createdAtUtc", "arrivedAtLocal", "screenedBy", "screenedAtUtc",
        "dispatchNoteReference", "bowserRegistration", "driverName", "dispatchedAtLocal", "dispatchedBy",
        "dispatchRecordedAtUtc", "totalDispatchedLitres", "tanks", "societiesByMargin", "missing"
    ];

    private static readonly string[] TankFields =
        ["tankCode", "tankName", "quantityDrawnLitres", "consignments", "missing"];

    private static readonly string[] ConsignmentFields =
    [
        "reference", "societyCode", "societyName", "canLabels", "quantityLitres", "quantityKg",
        "arrivalAtLocal", "registeredBy", "registeredAtUtc", "pouredAtUtc", "pouredBy", "qualityTest",
        "tightestMargin", "missing"
    ];

    private static readonly string[] QualityTestFields =
    [
        "fatPercent", "rawLactometerReading", "temperatureCelsius", "waterPercent", "kqColour",
        "correctedClr", "snf", "totalSolids", "stabilityGrade", "passedAlcoholAt", "verdict", "testedBy",
        "testedAtUtc", "margins"
    ];

    private static readonly string[] MarginFields = ["measure", "value", "threshold", "margin"];

    private static readonly string[] BatchFields =
    [
        "reference", "dispatchNoteReference", "batchDate", "arrivedAtLocal", "arrivalTemperatureCelsius",
        "screenedBy", "createdAtUtc"
    ];

    [Fact]
    public async Task The_trace_keeps_the_shape_Traceability_copied()
    {
        using var factory = new IntakeApiFactory();
        var trace = await TraceAsync(factory, await BatchTraceApiTests.BatchAsync(factory, ("T1", 0)));

        var tank = trace.GetProperty("tanks")[0];
        var consignment = tank.GetProperty("consignments")[0];
        var test = consignment.GetProperty("qualityTest");

        AssertFields(TraceFields, trace);
        AssertFields(TankFields, tank);
        AssertFields(ConsignmentFields, consignment);
        AssertFields(QualityTestFields, test);
        AssertFields(MarginFields, test.GetProperty("margins")[0]);

        // Enumerated values travel as their names.
        Assert.Equal("Accept", test.GetProperty("verdict").GetString());
        Assert.Equal("Blue", test.GetProperty("kqColour").GetString());
        Assert.Equal(JsonValueKind.String, test.GetProperty("stabilityGrade").ValueKind);
    }

    [Fact]
    public async Task A_batch_keeps_the_shape_Traceability_copied_on_both_lookups()
    {
        using var factory = new IntakeApiFactory();
        var batch = await BatchTraceApiTests.BatchAsync(factory, ("T1", 0));
        var note = (await TraceAsync(factory, batch)).GetProperty("dispatchNoteReference").GetString();

        var intake = factory.CreateClientAs(WonrichRoles.FactoryIntakeOfficer);
        var trace = factory.CreateClientAs(WonrichRoles.QualityAnalyst);

        var listed = await intake.GetFromJsonAsync<JsonElement>($"/api/factory/batches?dispatchNote={note}");
        var found = await trace.GetFromJsonAsync<JsonElement>($"/api/factory/batches/by-dispatch-note/{note}");

        AssertFields(BatchFields, listed[0]);
        Assert.Equal(listed.GetRawText(), found.GetRawText());
    }

    [Fact]
    public async Task An_unknown_batch_answers_404_with_entity_not_found()
    {
        using var factory = new IntakeApiFactory();

        var response = await factory.CreateClientAs(WonrichRoles.QualityAnalyst)
            .GetAsync("/api/factory/batches/WR-20260823-99/trace");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(
            "entity_not_found",
            (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Batches_are_listed_newest_first()
    {
        using var factory = new IntakeApiFactory();
        var day = DateOnly.FromDateTime(factory.Clock.LocalNow);

        var older = await BatchTraceApiTests.BatchAsync(factory, ("T1", 0));
        factory.Clock.LocalNow = factory.Clock.LocalNow.AddHours(1);
        var newer = await BatchTraceApiTests.BatchAsync(factory, ("T2", 1));

        var listed = await factory.CreateClientAs(WonrichRoles.FactoryIntakeOfficer)
            .GetFromJsonAsync<JsonElement>($"/api/factory/batches?date={day:yyyy-MM-dd}");

        Assert.Equal(
            [newer, older],
            listed.EnumerateArray().Select(batch => batch.GetProperty("reference").GetString()));
    }

    [Fact]
    public async Task Tanks_come_in_code_order_and_consignments_in_pour_order()
    {
        using var factory = new IntakeApiFactory();

        // Poured T2 first, so code order and pour order disagree about the tanks.
        var batch = await BatchTraceApiTests.BatchAsync(factory, ("T2", 0), ("T1", 2), ("T1", 1));
        var trace = await TraceAsync(factory, batch);

        var societies = await factory.CreateClientAs(WonrichRoles.QualityAnalyst)
            .GetFromJsonAsync<List<SocietyView>>("/api/societies");

        var tanks = trace.GetProperty("tanks").EnumerateArray().ToList();

        Assert.Equal(["T1", "T2"], tanks.Select(tank => tank.GetProperty("tankCode").GetString()));
        Assert.Equal(
            [societies![2].Code, societies[1].Code],
            tanks[0].GetProperty("consignments").EnumerateArray()
                .Select(consignment => consignment.GetProperty("societyCode").GetString()));
    }

    [Fact]
    public async Task Local_fields_are_factory_wall_clock_with_no_offset_and_Utc_fields_are_UTC()
    {
        using var factory = new IntakeApiFactory();
        var trace = await TraceAsync(factory, await BatchTraceApiTests.BatchAsync(factory, ("T1", 0)));

        // The screening stamps both from the same instant: arrival on the factory's wall clock,
        // the screening itself in UTC. Asia/Colombo is UTC+05:30 with no daylight saving.
        var arrivedLocal = trace.GetProperty("arrivedAtLocal").GetString()!;
        var screenedUtc = trace.GetProperty("screenedAtUtc").GetString()!;

        Assert.DoesNotMatch("(Z|[+-][0-9]{2}:[0-9]{2})$", arrivedLocal);

        // Read back from the database, so the UTC fields carry no "Z" either: UTC by name, not by suffix.
        Assert.DoesNotMatch("(Z|[+-][0-9]{2}:[0-9]{2})$", screenedUtc);
        Assert.Equal(factory.Clock.LocalNow, Parse(arrivedLocal));
        Assert.Equal(factory.Clock.UtcNow.UtcDateTime, Parse(screenedUtc));
        Assert.Equal(TimeSpan.FromMinutes(330), Parse(arrivedLocal) - Parse(screenedUtc));
    }

    [Fact]
    public async Task The_lookup_by_dispatch_note_needs_only_the_trace_policy()
    {
        using var factory = new IntakeApiFactory();
        var batch = await BatchTraceApiTests.BatchAsync(factory, ("T1", 0));
        var note = (await TraceAsync(factory, batch)).GetProperty("dispatchNoteReference").GetString();

        // Every TraceBatches role can resolve a note, so the service account needs no intake role.
        foreach (var role in new[] { WonrichRoles.QualityAnalyst, WonrichRoles.ProductionManager })
        {
            var found = await factory.CreateClientAs(role)
                .GetFromJsonAsync<JsonElement>($"/api/factory/batches/by-dispatch-note/{note}");

            Assert.Equal(batch, found[0].GetProperty("reference").GetString());
        }

        var refused = await factory.CreateClientAs(WonrichRoles.FactoryIntakeOfficer)
            .GetAsync($"/api/factory/batches/by-dispatch-note/{note}");
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);

        var unknown = await factory.CreateClientAs(WonrichRoles.QualityAnalyst)
            .GetFromJsonAsync<JsonElement>("/api/factory/batches/by-dispatch-note/DN-20260101-99");
        Assert.Equal(0, unknown.GetArrayLength());
    }

    private static async Task<JsonElement> TraceAsync(IntakeApiFactory factory, string batch) =>
        await factory.CreateClientAs(WonrichRoles.QualityAnalyst)
            .GetFromJsonAsync<JsonElement>($"/api/factory/batches/{batch}/trace");

    private static void AssertFields(string[] expected, JsonElement element) =>
        Assert.Equal(
            expected.Order(StringComparer.Ordinal),
            element.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));

    private static DateTime Parse(string value) =>
        DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
}
