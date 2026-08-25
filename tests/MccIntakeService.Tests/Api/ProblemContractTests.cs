using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MccIntakeService.Application.Societies;
using MccIntakeService.Tests.Support;

namespace MccIntakeService.Tests.Api;

/// <summary>
/// Locks down the published error contract against the responses actually returned (SCRUM-55).
/// The gap QA found was that the Swagger schema and the wire format had drifted apart, so these
/// tests assert both halves and that they agree.
/// </summary>
public class ProblemContractTests : IClassFixture<IntakeApiFactoryFixture>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IntakeApiFactory _factory;

    public ProblemContractTests(IntakeApiFactoryFixture fixture)
    {
        _factory = fixture.Factory;
    }

    private static async Task<JsonElement> SwaggerAsync(HttpClient client)
    {
        var document = await client.GetStringAsync("/swagger/v1/swagger.json");

        return JsonDocument.Parse(document).RootElement;
    }

    [Fact]
    public async Task The_documented_422_schema_includes_code_cutoff_and_arrival_time()
    {
        var client = _factory.CreateClient();
        var swagger = await SwaggerAsync(client);

        var properties = SwaggerSchema.PropertyNamesFor(swagger, "/api/consignments", "post", "422");

        Assert.Contains("code", properties);
        Assert.Contains("cutoff", properties);
        Assert.Contains("arrivalTime", properties);
    }

    [Fact]
    public async Task Error_responses_are_documented_as_problem_json_not_plain_json()
    {
        var client = _factory.CreateClient();
        var swagger = await SwaggerAsync(client);

        foreach (var status in new[] { "400", "422" })
        {
            var mediaTypes = SwaggerSchema.MediaTypes(
                SwaggerSchema.ResponseContent(swagger, "/api/consignments", "post", status));

            Assert.Contains("application/problem+json", mediaTypes);
            Assert.DoesNotContain("application/json", mediaTypes);
        }
    }

    [Fact]
    public async Task Success_responses_stay_documented_as_plain_json()
    {
        var client = _factory.CreateClient();
        var swagger = await SwaggerAsync(client);

        var mediaTypes = SwaggerSchema.MediaTypes(
            SwaggerSchema.ResponseContent(swagger, "/api/consignments", "post", "201"));

        Assert.Contains("application/json", mediaTypes);
    }

    [Fact]
    public async Task A_real_422_is_served_as_problem_json_with_every_documented_field()
    {
        using var factory = new IntakeApiFactory();
        factory.Clock.LocalNow = new DateTime(2026, 8, 23, 21, 20, 0);

        var client = factory.CreateClient();
        var societies = await client.GetFromJsonAsync<List<SocietyView>>("/api/societies", JsonOptions);

        var response = await client.PostAsJsonAsync("/api/consignments", new
        {
            societyId = societies!.First().Id,
            arrivalAtLocal = new DateTime(2026, 8, 23, 21, 13, 0),
            cans = new[] { new { canNumber = 1, quantityLitres = 40m } }
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("intake_cutoff_exceeded", problem.GetProperty("code").GetString());
        Assert.Equal("16:00", problem.GetProperty("cutoff").GetString());
        Assert.Equal("21:13", problem.GetProperty("arrivalTime").GetString());
    }

    [Fact]
    public async Task The_other_422_carries_code_but_no_cutoff_fields()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/consignments", new
        {
            societyId = Guid.NewGuid(),
            cans = new[] { new { canNumber = 1, quantityLitres = 40m } }
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        // code is the only field separating this from the cutoff failure, which is exactly why
        // it has to appear in the published schema.
        Assert.Equal("entity_not_found", problem.GetProperty("code").GetString());
        Assert.False(problem.TryGetProperty("cutoff", out _));
        Assert.False(problem.TryGetProperty("arrivalTime", out _));
    }

    [Fact]
    public async Task A_model_validation_400_is_also_served_as_problem_json()
    {
        var client = _factory.CreateClient();
        var societies = await client.GetFromJsonAsync<List<SocietyView>>("/api/societies", JsonOptions);

        var response = await client.PostAsJsonAsync("/api/consignments", new
        {
            societyId = societies!.First().Id,
            cans = Array.Empty<object>()
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task A_404_is_served_as_problem_json()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/consignments/MCC-20260823-KC-99");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }
}
