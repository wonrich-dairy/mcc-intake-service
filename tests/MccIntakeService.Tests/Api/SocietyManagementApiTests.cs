using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MccIntakeService.Application.Societies;
using MccIntakeService.Tests.Support;

namespace MccIntakeService.Tests.Api;

/// <summary>Drives the SCRUM-51 society management endpoints over HTTP.</summary>
public class SocietyManagementApiTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Each test gets its own host: these tests mutate the society list, so sharing one would
    /// leave the outcome depending on execution order.
    /// </summary>
    private static IntakeApiFactory NewFactory() => new();

    private static object NewSociety(string code = "TH", string? name = null) => new
    {
        code,
        name = name ?? "Thalawakele Tea Country Society",
        canLabelPrefix = code,
        contactPerson = "Sunil Perera",
        contactNumber = "+94 51 222 1111"
    };

    [Fact]
    public async Task Registering_a_society_returns_201_and_it_is_then_fetchable()
    {
        using var factory = NewFactory();
        var client = factory.CreateManagerClient();

        var response = await client.PostAsJsonAsync("/api/societies", NewSociety());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var created = await response.Content.ReadFromJsonAsync<SocietyView>(JsonOptions);
        Assert.NotNull(created);
        Assert.Equal("TH", created.Code);
        Assert.Equal("Sunil Perera", created.ContactPerson);

        var fetched = await client.GetFromJsonAsync<SocietyView>(response.Headers.Location!, JsonOptions);
        Assert.Equal(created.Id, fetched!.Id);
    }

    [Fact]
    public async Task Registering_a_society_on_a_code_already_in_use_returns_409()
    {
        using var factory = NewFactory();
        var client = factory.CreateManagerClient();

        var response = await client.PostAsJsonAsync("/api/societies", NewSociety("KC"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("duplicate_code", problem.GetProperty("code").GetString());
        Assert.Equal("KC", problem.GetProperty("conflictingCode").GetString());
    }

    [Fact]
    public async Task Registering_a_society_without_a_code_or_name_returns_400()
    {
        using var factory = NewFactory();
        var client = factory.CreateManagerClient();

        var response = await client.PostAsJsonAsync("/api/societies", new { code = "", name = "", canLabelPrefix = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_society_can_be_amended_over_http()
    {
        using var factory = NewFactory();
        var client = factory.CreateManagerClient();

        var created = await (await client.PostAsJsonAsync("/api/societies", NewSociety()))
            .Content.ReadFromJsonAsync<SocietyView>(JsonOptions);

        var response = await client.PutAsJsonAsync($"/api/societies/{created!.Id}", new
        {
            code = "TH",
            name = "Thalawakele Highland Society",
            canLabelPrefix = "TL",
            contactPerson = "Nimal Silva",
            contactNumber = "+94 51 222 9999"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var updated = await response.Content.ReadFromJsonAsync<SocietyView>(JsonOptions);
        Assert.Equal("Thalawakele Highland Society", updated!.Name);
        Assert.Equal("TL", updated.CanLabelPrefix);
    }

    [Fact]
    public async Task Amending_a_society_that_does_not_exist_returns_404()
    {
        using var factory = NewFactory();
        var client = factory.CreateManagerClient();

        var response = await client.PutAsJsonAsync($"/api/societies/{Guid.NewGuid()}", NewSociety());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Moving_a_code_that_consignments_depend_on_returns_400()
    {
        using var factory = NewFactory();
        var client = factory.CreateManagerClient();

        var societies = await client.GetFromJsonAsync<List<SocietyView>>("/api/societies", JsonOptions);
        var kandy = societies!.Single(society => society.Code == "KC");

        await client.PostAsJsonAsync("/api/consignments", new
        {
            societyId = kandy.Id,
            cans = new[] { new { canNumber = 1, quantityLitres = 40m } }
        });

        var response = await client.PutAsJsonAsync($"/api/societies/{kandy.Id}", new
        {
            code = "KD",
            name = kandy.Name,
            canLabelPrefix = kandy.CanLabelPrefix
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("cannot be changed", problem.GetProperty("detail").GetString()!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_society_can_be_retired_and_returned_to_service_over_http()
    {
        using var factory = NewFactory();
        var client = factory.CreateManagerClient();

        var created = await (await client.PostAsJsonAsync("/api/societies", NewSociety()))
            .Content.ReadFromJsonAsync<SocietyView>(JsonOptions);

        var deactivated = await (await client.PostAsync($"/api/societies/{created!.Id}/deactivate", null))
            .Content.ReadFromJsonAsync<SocietyView>(JsonOptions);
        Assert.False(deactivated!.IsActive);

        var active = await client.GetFromJsonAsync<List<SocietyView>>("/api/societies", JsonOptions);
        Assert.DoesNotContain(active!, society => society.Code == "TH");

        var all = await client.GetFromJsonAsync<List<SocietyView>>("/api/societies?includeInactive=true", JsonOptions);
        Assert.Contains(all!, society => society.Code == "TH");

        var reactivated = await (await client.PostAsync($"/api/societies/{created.Id}/reactivate", null))
            .Content.ReadFromJsonAsync<SocietyView>(JsonOptions);
        Assert.True(reactivated!.IsActive);
    }

    [Fact]
    public async Task Retiring_a_society_that_does_not_exist_returns_404()
    {
        using var factory = NewFactory();
        var client = factory.CreateManagerClient();

        var response = await client.PostAsync($"/api/societies/{Guid.NewGuid()}/deactivate", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task The_list_can_be_searched_and_sorted_over_http()
    {
        using var factory = NewFactory();
        var client = factory.CreateManagerClient();

        var searched = await client.GetFromJsonAsync<List<SocietyView>>("/api/societies?search=kandy", JsonOptions);
        Assert.Equal("KC", Assert.Single(searched!).Code);

        var descending = await client.GetFromJsonAsync<List<SocietyView>>(
            "/api/societies?sortBy=Code&descending=true", JsonOptions);
        Assert.Equal(["NW", "MT", "KC", "BD"], descending!.Select(society => society.Code));
    }

    [Fact]
    public async Task There_is_no_delete_endpoint_for_societies()
    {
        using var factory = NewFactory();
        var client = factory.CreateManagerClient();

        var societies = await client.GetFromJsonAsync<List<SocietyView>>("/api/societies", JsonOptions);

        var response = await client.DeleteAsync($"/api/societies/{societies![0].Id}");

        // Societies are retired, never removed, so history keeps resolving.
        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    [Fact]
    public async Task A_duplicate_code_conflict_is_served_as_problem_json()
    {
        using var factory = NewFactory();
        var client = factory.CreateManagerClient();

        var response = await client.PostAsJsonAsync("/api/societies", NewSociety("KC"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task The_documented_409_schema_carries_the_conflicting_code()
    {
        using var factory = NewFactory();
        var client = factory.CreateManagerClient();

        var swagger = JsonDocument.Parse(await client.GetStringAsync("/swagger/v1/swagger.json")).RootElement;

        var content = SwaggerSchema.ResponseContent(swagger, "/api/societies", "post", "409");
        Assert.Contains("application/problem+json", SwaggerSchema.MediaTypes(content));

        var properties = SwaggerSchema.PropertyNamesFor(swagger, "/api/societies", "post", "409");

        Assert.Contains("conflictingCode", properties);
        Assert.Contains("code", properties);
    }

    [Fact]
    public async Task The_swagger_document_lists_the_society_management_endpoints()
    {
        using var factory = NewFactory();
        var client = factory.CreateManagerClient();

        var document = await client.GetStringAsync("/swagger/v1/swagger.json");

        Assert.Contains("/api/societies/{id}/deactivate", document, StringComparison.Ordinal);
        Assert.Contains("/api/societies/{id}/reactivate", document, StringComparison.Ordinal);
    }
}
