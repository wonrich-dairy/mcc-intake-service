using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using MccIntakeService.Application.Societies;
using MccIntakeService.Tests.Support;
using Wonrich.Auth.Authorization;

namespace MccIntakeService.Tests.Api;

/// <summary>Drives the SCRUM-10 offline sync endpoint over HTTP.</summary>
public class SyncApiTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private static async Task<Guid> SocietyIdAsync(HttpClient client)
    {
        var societies = await client.GetFromJsonAsync<List<SocietyView>>("/api/societies", JsonOptions);

        return societies![0].Id;
    }

    private static object Registration(string clientId, int sequence, Guid societyId) => new
    {
        clientRecordId = clientId,
        sequence,
        kind = "RegisterConsignment",
        consignment = new
        {
            societyId,
            cans = new[] { new { canNumber = 1, quantityKg = 41.2m } },
            arrivalAtLocal = "2026-08-23T07:30:00"
        }
    };

    [Fact]
    public async Task A_morning_queue_uploaded_in_the_evening_still_applies()
    {
        using var factory = new IntakeApiFactory();
        var client = factory.CreateClientAs(WonrichRoles.IntakeOfficer);
        var society = await SocietyIdAsync(client);

        // Coverage came back long after the 16:00 cutoff. The record is judged on when the milk
        // was taken in, not on when the officer managed to upload it.
        factory.Clock.LocalNow = new DateTime(2026, 8, 23, 18, 0, 0);

        var response = await client.PostAsJsonAsync("/api/sync", new
        {
            operations = new[] { Registration("client-1", 1, society) }
        });

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(1, result.GetProperty("applied").GetInt32());
    }

    [Fact]
    public async Task A_queued_record_without_a_capture_time_is_refused()
    {
        using var factory = new IntakeApiFactory();
        var client = factory.CreateClientAs(WonrichRoles.IntakeOfficer);
        var society = await SocietyIdAsync(client);

        var response = await client.PostAsJsonAsync("/api/sync", new
        {
            operations = new[]
            {
                new
                {
                    clientRecordId = "client-1",
                    sequence = 1,
                    kind = "RegisterConsignment",
                    consignment = new
                    {
                        societyId = society,
                        cans = new[] { new { canNumber = 1, quantityKg = 41.2m } }
                    }
                }
            }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_queue_uploads_and_reports_each_record()
    {
        using var factory = new IntakeApiFactory();
        var client = factory.CreateClientAs(WonrichRoles.IntakeOfficer);
        var society = await SocietyIdAsync(client);

        var response = await client.PostAsJsonAsync("/api/sync", new
        {
            operations = new[] { Registration("client-1", 1, society) }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, result.GetProperty("applied").GetInt32());

        var record = result.GetProperty("results").EnumerateArray().Single();
        Assert.Equal("Applied", record.GetProperty("status").GetString());
        Assert.StartsWith("MCC-", record.GetProperty("reference").GetString());
    }

    [Fact]
    public async Task Replaying_a_queue_reports_duplicates_and_applies_nothing_twice()
    {
        using var factory = new IntakeApiFactory();
        var client = factory.CreateClientAs(WonrichRoles.IntakeOfficer);
        var society = await SocietyIdAsync(client);

        var body = new { operations = new[] { Registration("client-1", 1, society) } };

        var first = await (await client.PostAsJsonAsync("/api/sync", body))
            .Content.ReadFromJsonAsync<JsonElement>();
        var replay = await (await client.PostAsJsonAsync("/api/sync", body))
            .Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(1, replay.GetProperty("duplicates").GetInt32());
        Assert.Equal(0, replay.GetProperty("applied").GetInt32());

        Assert.Equal(
            first.GetProperty("results")[0].GetProperty("reference").GetString(),
            replay.GetProperty("results")[0].GetProperty("reference").GetString());

        var consignments = await client.GetFromJsonAsync<JsonElement>("/api/consignments", JsonOptions);
        Assert.Equal(1, consignments.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task A_failing_record_is_reported_without_sinking_the_rest()
    {
        using var factory = new IntakeApiFactory();
        var client = factory.CreateClientAs(WonrichRoles.IntakeOfficer);
        var society = await SocietyIdAsync(client);

        var response = await client.PostAsJsonAsync("/api/sync", new
        {
            operations = new object[]
            {
                new
                {
                    clientRecordId = "client-1",
                    sequence = 1,
                    kind = "PourToTank",
                    pour = new { tankCode = "T1", consignmentReference = "MCC-20260823-XX-99" }
                },
                Registration("client-2", 2, society)
            }
        });

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(1, result.GetProperty("failed").GetInt32());
        Assert.Equal(1, result.GetProperty("applied").GetInt32());

        var failed = result.GetProperty("results")[0];
        Assert.Equal("Failed", failed.GetProperty("status").GetString());
        Assert.False(string.IsNullOrWhiteSpace(failed.GetProperty("error").GetString()));
    }

    [Fact]
    public async Task An_upload_with_no_records_returns_400()
    {
        using var factory = new IntakeApiFactory();
        var client = factory.CreateClientAs(WonrichRoles.IntakeOfficer);

        var response = await client.PostAsJsonAsync("/api/sync", new { operations = Array.Empty<object>() });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_record_without_a_client_identifier_returns_400()
    {
        using var factory = new IntakeApiFactory();
        var client = factory.CreateClientAs(WonrichRoles.IntakeOfficer);
        var society = await SocietyIdAsync(client);

        var response = await client.PostAsJsonAsync("/api/sync", new
        {
            operations = new[] { Registration("", 1, society) }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_synced_consignment_is_readable_like_any_other()
    {
        using var factory = new IntakeApiFactory();
        var client = factory.CreateClientAs(WonrichRoles.IntakeOfficer);
        var society = await SocietyIdAsync(client);

        var result = await (await client.PostAsJsonAsync("/api/sync", new
        {
            operations = new[] { Registration("client-1", 1, society) }
        })).Content.ReadFromJsonAsync<JsonElement>();

        var reference = result.GetProperty("results")[0].GetProperty("reference").GetString();

        // Nothing about it reads as "arrived through the sync queue".
        var fetched = await client.GetAsync($"/api/consignments/{reference}");
        Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);
    }

    [Fact]
    public async Task An_unauthenticated_caller_is_refused()
    {
        using var factory = new IntakeApiFactory();

        var response = await factory.CreateClient()
            .PostAsJsonAsync("/api/sync", new { operations = Array.Empty<object>() });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_role_with_no_intake_duties_is_refused_with_403()
    {
        using var factory = new IntakeApiFactory();

        var response = await factory.CreateClientAs(WonrichRoles.ProductionManager)
            .PostAsJsonAsync("/api/sync", new { operations = Array.Empty<object>() });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task The_sync_endpoint_is_documented_in_swagger()
    {
        using var factory = new IntakeApiFactory();

        var document = await factory.CreateClient().GetStringAsync("/swagger/v1/swagger.json");

        Assert.Contains("/api/sync", document, StringComparison.Ordinal);
    }
}
