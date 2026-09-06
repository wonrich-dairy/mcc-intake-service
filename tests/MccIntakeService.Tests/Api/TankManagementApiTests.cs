using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using MccIntakeService.Application.Consignments;
using MccIntakeService.Application.Societies;
using MccIntakeService.Application.Tanks;
using MccIntakeService.Domain.Tanks;
using MccIntakeService.Tests.Support;
using Wonrich.Auth.Authorization;

namespace MccIntakeService.Tests.Api;

/// <summary>
/// Adding a tank, amending it, taking it out of service, and logging what it is holding at
/// (SCRUM-52). The plant changes; the client needed somewhere to say so.
/// </summary>
public class TankManagementApiTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private static object NewTank(string code = "T9", decimal capacity = 4000m) =>
        new { code, name = "Chilling Tank 9", capacityLitres = capacity };

    private static async Task<string> PourableConsignmentAsync(HttpClient client)
    {
        var societies = await client.GetFromJsonAsync<List<SocietyView>>("/api/societies", JsonOptions);

        var registered = await client.PostAsJsonAsync("/api/consignments", new
        {
            societyId = societies!.First().Id,
            cans = new[] { new { canNumber = 1, quantityKg = 41.2m } }
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

        return reference;
    }

    [Fact]
    public async Task A_manager_can_add_a_tank_and_it_joins_the_list()
    {
        using var factory = new IntakeApiFactory();
        var manager = factory.CreateClientAs(WonrichRoles.MccManager);

        var response = await manager.PostAsJsonAsync("/api/tanks", NewTank());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var tanks = await manager.GetFromJsonAsync<List<TankView>>("/api/tanks", JsonOptions);
        var added = tanks!.Single(tank => tank.Code == "T9");

        Assert.Equal("Chilling Tank 9", added.Name);
        Assert.Equal(4000m, added.CapacityLitres);
        Assert.Equal(TankStatus.Active, added.Status);
    }

    [Fact]
    public async Task An_intake_officer_may_pour_but_not_manage_tanks()
    {
        using var factory = new IntakeApiFactory();
        var officer = factory.CreateClientAs(WonrichRoles.IntakeOfficer);

        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await officer.PostAsJsonAsync("/api/tanks", NewTank())).StatusCode);

        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await officer.PutAsJsonAsync("/api/tanks/T1", NewTank("T1"))).StatusCode);

        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await officer.PostAsync("/api/tanks/T1/deactivate", null)).StatusCode);

        // Pouring is still theirs.
        Assert.Equal(HttpStatusCode.OK, (await officer.GetAsync("/api/tanks/pourable")).StatusCode);
    }

    [Fact]
    public async Task A_code_already_in_use_is_refused_with_409()
    {
        using var factory = new IntakeApiFactory();
        var manager = factory.CreateClientAs(WonrichRoles.MccManager);

        var response = await manager.PostAsJsonAsync("/api/tanks", NewTank("T1"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("duplicate_code", problem.GetProperty("code").GetString());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task A_capacity_that_is_not_a_volume_is_refused(decimal capacity)
    {
        using var factory = new IntakeApiFactory();
        var manager = factory.CreateClientAs(WonrichRoles.MccManager);

        var response = await manager.PostAsJsonAsync("/api/tanks", NewTank("T8", capacity));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Amending_a_tank_renames_it_and_leaves_the_code_alone()
    {
        using var factory = new IntakeApiFactory();
        var manager = factory.CreateClientAs(WonrichRoles.MccManager);

        var response = await manager.PutAsJsonAsync(
            "/api/tanks/T1",
            new { code = "RENAMED", name = "Primary Cooler", capacityLitres = 6000m });

        response.EnsureSuccessStatusCode();
        var tank = await response.Content.ReadFromJsonAsync<TankView>(JsonOptions);

        Assert.Equal("T1", tank!.Code);
        Assert.Equal("Primary Cooler", tank.Name);
        Assert.Equal(6000m, tank.CapacityLitres);
    }

    [Fact]
    public async Task A_tank_out_of_service_refuses_a_pour()
    {
        using var factory = new IntakeApiFactory();
        var manager = factory.CreateClientAs(WonrichRoles.MccManager);
        var officer = factory.CreateClientAs(WonrichRoles.IntakeOfficer);

        var reference = await PourableConsignmentAsync(officer);

        var closed = await manager.PostAsync("/api/tanks/T2/deactivate", null);
        closed.EnsureSuccessStatusCode();
        Assert.Equal(
            TankStatus.UnderMaintenance,
            (await closed.Content.ReadFromJsonAsync<TankView>(JsonOptions))!.Status);

        var pour = await officer.PostAsJsonAsync(
            "/api/tanks/T2/pours",
            new { consignmentReference = reference });

        Assert.Equal(HttpStatusCode.BadRequest, pour.StatusCode);

        // Back in service, the same pour is accepted.
        (await manager.PostAsync("/api/tanks/T2/reactivate", null)).EnsureSuccessStatusCode();

        var second = await officer.PostAsJsonAsync(
            "/api/tanks/T2/pours",
            new { consignmentReference = reference });

        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
    }

    [Fact]
    public async Task A_tank_still_holding_milk_cannot_be_taken_out_of_service()
    {
        using var factory = new IntakeApiFactory();
        var manager = factory.CreateClientAs(WonrichRoles.MccManager);
        var officer = factory.CreateClientAs(WonrichRoles.IntakeOfficer);

        var reference = await PourableConsignmentAsync(officer);

        (await officer.PostAsJsonAsync("/api/tanks/T1/pours", new { consignmentReference = reference }))
            .EnsureSuccessStatusCode();

        var response = await manager.PostAsync("/api/tanks/T1/deactivate", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_reading_is_recorded_and_read_back_newest_first()
    {
        using var factory = new IntakeApiFactory();
        var officer = factory.CreateClientAs(WonrichRoles.IntakeOfficer);

        foreach (var celsius in new[] { 4.2m, 3.5m, 3.0m })
        {
            factory.Clock.LocalNow = factory.Clock.LocalNow.AddMinutes(30);

            (await officer.PostAsJsonAsync("/api/tanks/T1/temperatures", new { celsius }))
                .EnsureSuccessStatusCode();
        }

        var readings = await officer.GetFromJsonAsync<List<TankTemperatureView>>(
            "/api/tanks/T1/temperatures", JsonOptions);

        Assert.Equal([3.0m, 3.5m, 4.2m], readings!.Select(reading => reading.Celsius));
        Assert.All(readings, reading => Assert.Equal("test-user", reading.RecordedBy));
    }

    [Fact]
    public async Task The_tank_list_carries_the_latest_reading()
    {
        using var factory = new IntakeApiFactory();
        var officer = factory.CreateClientAs(WonrichRoles.IntakeOfficer);

        var before = await officer.GetFromJsonAsync<List<TankView>>("/api/tanks", JsonOptions);
        Assert.Null(before!.Single(tank => tank.Code == "T1").LatestTemperature);

        (await officer.PostAsJsonAsync("/api/tanks/T1/temperatures", new { celsius = 3.8m }))
            .EnsureSuccessStatusCode();

        var after = await officer.GetFromJsonAsync<List<TankView>>("/api/tanks", JsonOptions);

        Assert.Equal(3.8m, after!.Single(tank => tank.Code == "T1").LatestTemperature!.Celsius);
        Assert.Null(after.Single(tank => tank.Code == "T2").LatestTemperature);
    }

    [Theory]
    [InlineData(-40)]
    [InlineData(80)]
    public async Task A_reading_no_instrument_would_report_is_refused(decimal celsius)
    {
        using var factory = new IntakeApiFactory();
        var officer = factory.CreateClientAs(WonrichRoles.IntakeOfficer);

        var response = await officer.PostAsJsonAsync("/api/tanks/T1/temperatures", new { celsius });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Readings_and_management_on_an_unknown_tank_answer_404()
    {
        using var factory = new IntakeApiFactory();
        var manager = factory.CreateClientAs(WonrichRoles.MccManager);

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await manager.GetAsync("/api/tanks/NOPE/temperatures")).StatusCode);

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await manager.PostAsJsonAsync("/api/tanks/NOPE/temperatures", new { celsius = 3.5m })).StatusCode);

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await manager.PostAsync("/api/tanks/NOPE/deactivate", null)).StatusCode);
    }
}
