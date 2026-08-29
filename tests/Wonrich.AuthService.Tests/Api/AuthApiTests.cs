using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Wonrich.AuthService.Controllers;
using Wonrich.AuthService.Tests.Support;

namespace Wonrich.AuthService.Tests.Api;

/// <summary>Drives sign-in and token renewal over HTTP (SCRUM-34).</summary>
public class AuthApiTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static async Task<TokenResponse> SignInAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            userName = "k.perera",
            password = AuthTestHost.KnownPassword
        });

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<TokenResponse>(JsonOptions))!;
    }

    [Fact]
    public async Task Signing_in_returns_both_tokens_and_their_expiry()
    {
        using var factory = new AuthApiFactory();

        var tokens = await SignInAsync(factory.CreateClient());

        Assert.NotEmpty(tokens.AccessToken);
        Assert.NotEmpty(tokens.RefreshToken);
        Assert.True(tokens.ExpiresAtUtc > DateTime.UtcNow);
        Assert.True(tokens.RefreshExpiresAtUtc > tokens.ExpiresAtUtc);
    }

    [Fact]
    public async Task A_wrong_password_returns_401()
    {
        using var factory = new AuthApiFactory();

        var response = await factory.CreateClient().PostAsJsonAsync("/api/auth/login", new
        {
            userName = "k.perera",
            password = "wrong"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task An_unknown_user_returns_401()
    {
        using var factory = new AuthApiFactory();

        var response = await factory.CreateClient().PostAsJsonAsync("/api/auth/login", new
        {
            userName = "nobody",
            password = AuthTestHost.KnownPassword
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_request_missing_credentials_returns_400()
    {
        using var factory = new AuthApiFactory();

        var response = await factory.CreateClient()
            .PostAsJsonAsync("/api/auth/login", new { userName = "", password = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_refresh_token_can_be_exchanged_over_http()
    {
        using var factory = new AuthApiFactory();
        var client = factory.CreateClient();

        var tokens = await SignInAsync(client);

        var response = await client.PostAsJsonAsync("/api/auth/refresh", new
        {
            refreshToken = tokens.RefreshToken
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var renewed = await response.Content.ReadFromJsonAsync<TokenResponse>(JsonOptions);
        Assert.NotEqual(tokens.RefreshToken, renewed!.RefreshToken);
    }

    [Fact]
    public async Task Replaying_a_spent_refresh_token_returns_401()
    {
        using var factory = new AuthApiFactory();
        var client = factory.CreateClient();

        var tokens = await SignInAsync(client);
        var body = new { refreshToken = tokens.RefreshToken };

        await client.PostAsJsonAsync("/api/auth/refresh", body);
        var replay = await client.PostAsJsonAsync("/api/auth/refresh", body);

        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
    }

    [Fact]
    public async Task An_unknown_refresh_token_returns_401()
    {
        using var factory = new AuthApiFactory();

        var response = await factory.CreateClient()
            .PostAsJsonAsync("/api/auth/refresh", new { refreshToken = "not-a-real-token" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_refresh_request_with_no_token_returns_400()
    {
        using var factory = new AuthApiFactory();

        var response = await factory.CreateClient()
            .PostAsJsonAsync("/api/auth/refresh", new { refreshToken = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task The_response_never_echoes_the_password()
    {
        using var factory = new AuthApiFactory();

        var response = await factory.CreateClient().PostAsJsonAsync("/api/auth/login", new
        {
            userName = "k.perera",
            password = AuthTestHost.KnownPassword
        });

        var body = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain(AuthTestHost.KnownPassword, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_auth_endpoints_are_documented_in_swagger()
    {
        using var factory = new AuthApiFactory();

        var document = await factory.CreateClient().GetStringAsync("/swagger/v1/swagger.json");

        Assert.Contains("/api/auth/login", document, StringComparison.Ordinal);
        Assert.Contains("/api/auth/refresh", document, StringComparison.Ordinal);
    }
}
