using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Wonrich.Auth.Authorization;
using Wonrich.AuthService.Application;
using Wonrich.AuthService.Tests.Support;

namespace Wonrich.AuthService.Tests.Api;

/// <summary>Drives the SCRUM-45 account administration endpoints over HTTP.</summary>
public class UsersApiTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Stands in for an account identifier; authorization refuses before it is read.</summary>
    private static readonly Guid SomeAccount = new("6f0f6f1a-0045-4a2b-9c3d-000000000001");

    private static object NewUser(string userName = "n.silva", string role = WonrichRoles.IntakeOfficer) => new
    {
        userName,
        displayName = "Nimal Silva",
        password = "a-long-enough-password",
        role,
        facility = "MCC-KANDY"
    };

    /// <summary>
    /// Every endpoint the ManageUsers policy guards. The policy is declared on the controller, so
    /// a route left out here is a route nobody checks answers 401 — the identifier is never looked
    /// up, because authorization runs first.
    /// </summary>
    public static TheoryData<string, string> GuardedEndpoints() => new()
    {
        { "GET", "/api/users" },
        { "GET", "/api/users/roles" },
        { "GET", $"/api/users/{SomeAccount}" },
        { "POST", "/api/users" },
        { "PUT", $"/api/users/{SomeAccount}" },
        { "POST", $"/api/users/{SomeAccount}/deactivate" },
        { "POST", $"/api/users/{SomeAccount}/reactivate" }
    };

    [Theory]
    [MemberData(nameof(GuardedEndpoints))]
    public async Task An_unauthenticated_caller_is_refused_with_401(string method, string route)
    {
        using var factory = new AuthApiFactory();
        var anonymous = factory.CreateClient();

        var response = method switch
        {
            "GET" => await anonymous.GetAsync(route),
            "PUT" => await anonymous.PutAsJsonAsync(route, NewUser()),
            _ => await anonymous.PostAsJsonAsync(route, NewUser())
        };

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(WonrichRoles.MccManager)]
    [InlineData(WonrichRoles.IntakeOfficer)]
    [InlineData(WonrichRoles.QualityAnalyst)]
    [InlineData(WonrichRoles.FactoryIntakeOfficer)]
    [InlineData(WonrichRoles.ProductionManager)]
    public async Task Only_an_administrator_may_administer_accounts(string role)
    {
        using var factory = new AuthApiFactory();
        var client = factory.CreateClientAs(role);

        var response = await client.PostAsJsonAsync("/api/users", NewUser());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task An_administrator_can_create_and_then_fetch_an_account()
    {
        using var factory = new AuthApiFactory();
        var admin = factory.CreateClientAs(WonrichRoles.SystemAdministrator);

        var response = await admin.PostAsJsonAsync("/api/users", NewUser());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var created = await response.Content.ReadFromJsonAsync<UserView>(JsonOptions);
        Assert.Equal("n.silva", created!.UserName);

        var fetched = await admin.GetFromJsonAsync<UserView>(response.Headers.Location!, JsonOptions);
        Assert.Equal(created.Id, fetched!.Id);
    }

    [Fact]
    public async Task A_created_account_can_immediately_sign_in()
    {
        using var factory = new AuthApiFactory();
        var admin = factory.CreateClientAs(WonrichRoles.SystemAdministrator);

        await admin.PostAsJsonAsync("/api/users", NewUser());

        // The whole point of SCRUM-45: accounts made here can actually log in.
        var login = await factory.CreateClient().PostAsJsonAsync("/api/auth/login", new
        {
            userName = "n.silva",
            password = "a-long-enough-password"
        });

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    [Fact]
    public async Task A_deactivated_account_can_no_longer_sign_in()
    {
        using var factory = new AuthApiFactory();
        var admin = factory.CreateClientAs(WonrichRoles.SystemAdministrator);

        var created = await (await admin.PostAsJsonAsync("/api/users", NewUser()))
            .Content.ReadFromJsonAsync<UserView>(JsonOptions);

        await admin.PostAsync($"/api/users/{created!.Id}/deactivate", null);

        var login = await factory.CreateClient().PostAsJsonAsync("/api/auth/login", new
        {
            userName = "n.silva",
            password = "a-long-enough-password"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
    }

    [Fact]
    public async Task A_duplicate_username_returns_409()
    {
        using var factory = new AuthApiFactory();
        var admin = factory.CreateClientAs(WonrichRoles.SystemAdministrator);

        await admin.PostAsJsonAsync("/api/users", NewUser());
        var response = await admin.PostAsJsonAsync("/api/users", NewUser());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task An_unconfigured_role_returns_400()
    {
        using var factory = new AuthApiFactory();
        var admin = factory.CreateClientAs(WonrichRoles.SystemAdministrator);

        var response = await admin.PostAsJsonAsync("/api/users", NewUser(role: "Chief Cheese Taster"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// A field the domain rejects and a field model validation rejects both answer 400, and both
    /// are documented as ValidationProblemDetails, so a client reads `errors` for either.
    /// </summary>
    [Fact]
    public async Task A_rejected_field_is_reported_in_the_documented_validation_shape()
    {
        using var factory = new AuthApiFactory();
        var admin = factory.CreateClientAs(WonrichRoles.SystemAdministrator);

        var response = await admin.PostAsJsonAsync("/api/users", NewUser(role: "Chief Cheese Taster"));
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(JsonOptions);

        var error = Assert.Single(problem!.Errors);
        Assert.Equal("role", error.Key);
        Assert.Contains("Chief Cheese Taster", Assert.Single(error.Value));
        Assert.DoesNotContain("Parameter", Assert.Single(error.Value));
    }

    [Fact]
    public async Task A_short_password_returns_400()
    {
        using var factory = new AuthApiFactory();
        var admin = factory.CreateClientAs(WonrichRoles.SystemAdministrator);

        var response = await admin.PostAsJsonAsync("/api/users", new
        {
            userName = "x.short",
            displayName = "Short Password",
            password = "tooshort",
            role = WonrichRoles.IntakeOfficer
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task An_account_can_be_amended_over_http()
    {
        using var factory = new AuthApiFactory();
        var admin = factory.CreateClientAs(WonrichRoles.SystemAdministrator);

        var created = await (await admin.PostAsJsonAsync("/api/users", NewUser()))
            .Content.ReadFromJsonAsync<UserView>(JsonOptions);

        var response = await admin.PutAsJsonAsync($"/api/users/{created!.Id}", new
        {
            displayName = "Nimal T. Silva",
            role = WonrichRoles.QualityAnalyst,
            facility = "MCC-NUWARA"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var updated = await response.Content.ReadFromJsonAsync<UserView>(JsonOptions);
        Assert.Equal(WonrichRoles.QualityAnalyst, updated!.Role);
        Assert.Equal("Nimal T. Silva", updated.DisplayName);
    }

    [Fact]
    public async Task Addressing_an_account_that_does_not_exist_returns_404()
    {
        using var factory = new AuthApiFactory();
        var admin = factory.CreateClientAs(WonrichRoles.SystemAdministrator);

        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/users/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await admin.PostAsync($"/api/users/{Guid.NewGuid()}/deactivate", null)).StatusCode);
    }

    [Fact]
    public async Task The_list_can_be_searched_and_filtered_by_role_over_http()
    {
        using var factory = new AuthApiFactory();
        var admin = factory.CreateClientAs(WonrichRoles.SystemAdministrator);

        await admin.PostAsJsonAsync("/api/users", NewUser());
        await admin.PostAsJsonAsync("/api/users", new
        {
            userName = "s.fernando",
            displayName = "Sunil Fernando",
            password = "another-long-password",
            role = WonrichRoles.FactoryIntakeOfficer
        });

        var searched = await admin.GetFromJsonAsync<List<UserView>>("/api/users?search=silva", JsonOptions);
        Assert.Equal("n.silva", Assert.Single(searched!).UserName);

        var filtered = await admin.GetFromJsonAsync<List<UserView>>(
            $"/api/users?role={WonrichRoles.FactoryIntakeOfficer}", JsonOptions);
        Assert.Equal("s.fernando", Assert.Single(filtered!).UserName);
    }

    [Fact]
    public async Task The_role_picker_offers_all_six_roles()
    {
        using var factory = new AuthApiFactory();
        var admin = factory.CreateClientAs(WonrichRoles.SystemAdministrator);

        var roles = await admin.GetFromJsonAsync<List<string>>("/api/users/roles", JsonOptions);

        Assert.Equal(6, roles!.Count);
        Assert.Equal(WonrichRoles.All, roles);
    }

    [Fact]
    public async Task There_is_no_delete_endpoint_for_accounts()
    {
        using var factory = new AuthApiFactory();
        var admin = factory.CreateClientAs(WonrichRoles.SystemAdministrator);

        var created = await (await admin.PostAsJsonAsync("/api/users", NewUser()))
            .Content.ReadFromJsonAsync<UserView>(JsonOptions);

        var response = await admin.DeleteAsync($"/api/users/{created!.Id}");

        // Accounts are deactivated, never removed, so sign-in history keeps resolving.
        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    [Fact]
    public async Task A_response_never_carries_the_password_or_its_hash()
    {
        using var factory = new AuthApiFactory();
        var admin = factory.CreateClientAs(WonrichRoles.SystemAdministrator);

        var response = await admin.PostAsJsonAsync("/api/users", NewUser());
        var body = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("a-long-enough-password", body, StringComparison.Ordinal);
        Assert.DoesNotContain("pbkdf2", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_account_endpoints_are_documented_in_swagger()
    {
        using var factory = new AuthApiFactory();

        var document = await factory.CreateClient().GetStringAsync("/swagger/v1/swagger.json");

        Assert.Contains("/api/users", document, StringComparison.Ordinal);
        Assert.Contains("/api/users/{id}/deactivate", document, StringComparison.Ordinal);
        Assert.Contains("/api/users/{id}/reactivate", document, StringComparison.Ordinal);
    }
}
