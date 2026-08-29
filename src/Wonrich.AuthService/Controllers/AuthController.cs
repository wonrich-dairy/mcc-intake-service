using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wonrich.Auth.Tokens;
using Wonrich.AuthService.Application;

namespace Wonrich.AuthService.Controllers;

/// <summary>Credentials submitted at sign-in.</summary>
public sealed class LoginRequest
{
    /// <example>k.perera</example>
    [Required(ErrorMessage = "A username is required.")]
    public string UserName { get; set; } = string.Empty;

    /// <example>correct-horse-battery-staple</example>
    [Required(ErrorMessage = "A password is required.")]
    public string Password { get; set; } = string.Empty;
}

/// <summary>A refresh token being exchanged for a new access token.</summary>
public sealed class RefreshRequest
{
    [Required(ErrorMessage = "A refresh token is required.")]
    public string RefreshToken { get; set; } = string.Empty;
}

/// <summary>The tokens handed back on a successful sign-in or refresh.</summary>
/// <param name="AccessToken">Signed JWT to present as a bearer token.</param>
/// <param name="ExpiresAtUtc">When the access token stops being accepted.</param>
/// <param name="RefreshToken">Single-use token that buys a new access token.</param>
/// <param name="RefreshExpiresAtUtc">When the refresh token stops being accepted.</param>
public sealed record TokenResponse(
    string AccessToken,
    DateTime ExpiresAtUtc,
    string RefreshToken,
    DateTime RefreshExpiresAtUtc);

/// <summary>
/// Sign-in and token renewal for every Wonrich service (SCRUM-34). Tokens issued here are
/// validated independently by each service, which never calls back to this one.
/// </summary>
[ApiController]
[Route("api/auth")]
[AllowAnonymous]
public class AuthController : ControllerBase
{
    private const string ProblemJson = "application/problem+json";

    private readonly IAuthenticationService _authentication;

    public AuthController(IAuthenticationService authentication)
    {
        _authentication = authentication;
    }

    /// <summary>Signs in with a username and password.</summary>
    /// <response code="200">The credentials were accepted.</response>
    /// <response code="400">The request is missing a username or password.</response>
    /// <response code="401">The credentials were refused.</response>
    [HttpPost("login")]
    [ProducesResponseType(typeof(TokenResponse), StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest, ProblemJson)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<TokenResponse>> Login(
        [FromBody] LoginRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _authentication.SignInAsync(
            request.UserName,
            request.Password,
            SourceOfRequest(),
            cancellationToken);

        return Answer(result, "Those credentials were not accepted.");
    }

    /// <summary>Exchanges a refresh token for a fresh pair of tokens.</summary>
    /// <remarks>
    /// Refresh tokens are single use. Exchanging one revokes it and returns a replacement, so a
    /// stolen token stops working as soon as the real user refreshes.
    /// </remarks>
    /// <response code="200">The refresh token was accepted.</response>
    /// <response code="400">No refresh token was supplied.</response>
    /// <response code="401">The refresh token is unknown, spent or expired.</response>
    [HttpPost("refresh")]
    [ProducesResponseType(typeof(TokenResponse), StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest, ProblemJson)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<TokenResponse>> Refresh(
        [FromBody] RefreshRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _authentication.RefreshAsync(
            request.RefreshToken,
            SourceOfRequest(),
            cancellationToken);

        return Answer(result, "That refresh token is no longer valid.");
    }

    private ActionResult<TokenResponse> Answer(AuthResult result, string refusal)
    {
        if (!result.Succeeded)
        {
            // One shape for every refusal: distinguishing them would tell a caller whether a
            // username exists.
            return Problem(statusCode: StatusCodes.Status401Unauthorized, title: refusal);
        }

        var tokens = result.Tokens!;

        return Ok(new TokenResponse(
            tokens.AccessToken,
            tokens.ExpiresAtUtc,
            tokens.RefreshToken,
            tokens.RefreshExpiresAtUtc));
    }

    /// <summary>Where the attempt came from, for the failed-attempt log.</summary>
    private string? SourceOfRequest() => HttpContext.Connection.RemoteIpAddress?.ToString();
}
