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

    private async Task<JsonElement> SwaggerAsync(HttpClient client)
    {
        var document = await client.GetStringAsync("/swagger/v1/swagger.json");

        return JsonDocument.Parse(document).RootElement;
    }

    private static JsonElement ResponseSchema(JsonElement swagger, string path, string verb, string status)
    {
        var response = swagger.GetProperty("paths").GetProperty(path).GetProperty(verb)
            .GetProperty("responses").GetProperty(status);

        return response.GetProperty("content");
    }

    /// <summary>Walks $ref / allOf so an inherited schema reports every property it exposes.</summary>
    private static HashSet<string> PropertyNames(JsonElement swagger, JsonElement schema)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);

        if (schema.TryGetProperty("$ref", out var reference))
        {
            var name = reference.GetString()!.Split('/')[^1];
            var resolved = swagger.GetProperty("components").GetProperty("schemas").GetProperty(name);

            foreach (var property in PropertyNames(swagger, resolved))
            {
                names.Add(property);
            }
        }

        if (schema.TryGetProperty("allOf", out var allOf))
        {
            foreach (var member in allOf.EnumerateArray())
            {
                foreach (var property in PropertyNames(swagger, member))
                {
                    names.Add(property);
                }
            }
        }

        if (schema.TryGetProperty("properties", out var properties))
        {
            foreach (var property in properties.EnumerateObject())
            {
                names.Add(property.Name);
            }
        }

        return names;
    }

    [Fact]
    public async Task The_documented_422_schema_includes_code_cutoff_and_arrival_time()
    {
        var client = _factory.CreateClient();
        var swagger = await SwaggerAsync(client);

        var content = ResponseSchema(swagger, "/api/consignments", "post", "422");
        var schema = content.GetProperty("application/problem+json").GetProperty("schema");

        var properties = PropertyNames(swagger, schema);

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
            var content = ResponseSchema(swagger, "/api/consignments", "post", status);
            var mediaTypes = content.EnumerateObject().Select(media => media.Name).ToList();

            Assert.Contains("application/problem+json", mediaTypes);
            Assert.DoesNotContain("application/json", mediaTypes);
        }
    }

    [Fact]
    public async Task Success_responses_stay_documented_as_plain_json()
    {
        var client = _factory.CreateClient();
        var swagger = await SwaggerAsync(client);

        var content = ResponseSchema(swagger, "/api/consignments", "post", "201");

        Assert.Contains("application/json", content.EnumerateObject().Select(media => media.Name));
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
