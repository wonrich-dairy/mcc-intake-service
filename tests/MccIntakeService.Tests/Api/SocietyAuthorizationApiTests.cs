using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MccIntakeService.Api.Infrastructure;
using MccIntakeService.Application.Societies;
using MccIntakeService.Tests.Support;

namespace MccIntakeService.Tests.Api;

/// <summary>
/// Covers the SCRUM-51 rule that only MCC Managers and System Administrators may maintain
/// societies, while anyone may read the list to pick a society at the gate.
/// </summary>
public class SocietyAuthorizationApiTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static IntakeApiFactory NewFactory() => new();

    private static object NewSociety(string code = "TH") => new
    {
        code,
        name = "Thalawakele Tea Country Society",
        canLabelPrefix = code
    };

    /// <summary>The four endpoints the policy guards, each paired with a request that reaches it.</summary>
    public static TheoryData<string, string> GuardedEndpoints() => new()
    {
        { "POST", "/api/societies" },
        { "PUT", "/api/societies/{id}" },
        { "POST", "/api/societies/{id}/deactivate" },
        { "POST", "/api/societies/{id}/reactivate" }
    };

    [Theory]
    [MemberData(nameof(GuardedEndpoints))]
    public async Task An_unauthenticated_caller_is_refused_with_401(string method, string route)
    {
        using var factory = NewFactory();
        var anonymous = factory.CreateClient();

        var response = await SendAsync(anonymous, method, await ResolveAsync(factory, route));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(GuardedEndpoints))]
    public async Task An_intake_officer_is_refused_with_403(string method, string route)
    {
        using var factory = NewFactory();
        var officer = factory.CreateClientAs(IntakeRoles.IntakeOfficer);

        var response = await SendAsync(officer, method, await ResolveAsync(factory, route));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task An_mcc_manager_may_register_a_society()
    {
        using var factory = NewFactory();
        var manager = factory.CreateClientAs(IntakeRoles.MccManager);

        var response = await manager.PostAsJsonAsync("/api/societies", NewSociety());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task A_system_administrator_may_register_a_society()
    {
        using var factory = NewFactory();
        var administrator = factory.CreateClientAs(IntakeRoles.SystemAdministrator);

        var response = await administrator.PostAsJsonAsync("/api/societies", NewSociety("NV"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task A_caller_holding_several_roles_is_allowed_on_the_strength_of_one()
    {
        using var factory = NewFactory();
        var client = factory.CreateClientAs(IntakeRoles.IntakeOfficer, IntakeRoles.MccManager);

        var response = await client.PostAsJsonAsync("/api/societies", NewSociety("HP"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Reading_the_society_list_stays_open_to_an_intake_officer()
    {
        using var factory = NewFactory();
        var officer = factory.CreateClientAs(IntakeRoles.IntakeOfficer);

        // The officer has to see the list to pick a society when registering a consignment.
        var societies = await officer.GetFromJsonAsync<List<SocietyView>>("/api/societies", JsonOptions);

        Assert.NotEmpty(societies!);
    }

    [Fact]
    public async Task Reading_a_single_society_stays_open_to_an_unauthenticated_caller()
    {
        using var factory = NewFactory();
        var anonymous = factory.CreateClient();

        var societies = await anonymous.GetFromJsonAsync<List<SocietyView>>("/api/societies", JsonOptions);
        var response = await anonymous.GetAsync($"/api/societies/{societies![0].Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task The_swagger_document_declares_the_refusals_on_a_guarded_endpoint()
    {
        using var factory = NewFactory();
        var client = factory.CreateClient();

        var swagger = JsonDocument.Parse(await client.GetStringAsync("/swagger/v1/swagger.json")).RootElement;

        var responses = swagger
            .GetProperty("paths")
            .GetProperty("/api/societies")
            .GetProperty("post")
            .GetProperty("responses");

        Assert.True(responses.TryGetProperty("401", out _));
        Assert.True(responses.TryGetProperty("403", out _));
    }

    /// <summary>Substitutes a real society id into the route templates above.</summary>
    private static async Task<string> ResolveAsync(IntakeApiFactory factory, string route)
    {
        if (!route.Contains("{id}", StringComparison.Ordinal))
        {
            return route;
        }

        var societies = await factory.CreateClient()
            .GetFromJsonAsync<List<SocietyView>>("/api/societies", JsonOptions);

        return route.Replace("{id}", societies![0].Id.ToString(), StringComparison.Ordinal);
    }

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, string method, string route) =>
        method == "PUT"
            ? client.PutAsJsonAsync(route, NewSociety())
            : route.EndsWith("/api/societies", StringComparison.Ordinal)
                ? client.PostAsJsonAsync(route, NewSociety())
                : client.PostAsync(route, null);
}
