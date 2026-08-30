using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wonrich.Auth.Authorization;
using Wonrich.AuthService.Application;
using Wonrich.AuthService.Domain;

namespace Wonrich.AuthService.Controllers;

/// <summary>Policies this service enforces on its own endpoints.</summary>
public static class AuthPolicies
{
    /// <summary>Create, amend, deactivate or reactivate a user account (SCRUM-45).</summary>
    public const string ManageUsers = "ManageUsers";
}

/// <summary>Details supplied when creating an account.</summary>
public sealed class CreateUserRequest
{
    /// <summary>Sign-in name. Stored lower-cased and unique across the system.</summary>
    /// <example>k.perera</example>
    [Required(ErrorMessage = "A username is required.")]
    [StringLength(UserAccount.MaxUserNameLength, MinimumLength = 2)]
    public string UserName { get; set; } = string.Empty;

    /// <example>Kamal Perera</example>
    [Required(ErrorMessage = "A display name is required.")]
    [StringLength(UserAccount.MaxDisplayNameLength, MinimumLength = 1)]
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Initial password. Stored only as a PBKDF2 hash.</summary>
    [Required(ErrorMessage = "A password is required.")]
    [StringLength(200, MinimumLength = 12, ErrorMessage = "A password must be at least 12 characters.")]
    public string Password { get; set; } = string.Empty;

    /// <summary>One of the six configured roles.</summary>
    /// <example>IntakeOfficer</example>
    [Required(ErrorMessage = "A role is required.")]
    public string Role { get; set; } = string.Empty;

    /// <summary>Chilling centre or factory the user operates at.</summary>
    /// <example>MCC-KANDY</example>
    [StringLength(UserAccount.MaxFacilityLength)]
    public string? Facility { get; set; }
}

/// <summary>Details supplied when amending an account.</summary>
public sealed class UpdateUserRequest
{
    [Required(ErrorMessage = "A display name is required.")]
    [StringLength(UserAccount.MaxDisplayNameLength, MinimumLength = 1)]
    public string DisplayName { get; set; } = string.Empty;

    [Required(ErrorMessage = "A role is required.")]
    public string Role { get; set; } = string.Empty;

    [StringLength(UserAccount.MaxFacilityLength)]
    public string? Facility { get; set; }

    /// <summary>Supply to reset the password; omit to leave it unchanged.</summary>
    [StringLength(200, MinimumLength = 12, ErrorMessage = "A password must be at least 12 characters.")]
    public string? NewPassword { get; set; }
}

/// <summary>
/// Administration of user accounts and their role assignments (SCRUM-45). Accounts are
/// deactivated, never deleted, so sign-in history keeps resolving to the account that made it.
/// </summary>
[ApiController]
[Route("api/users")]
[Authorize(Policy = AuthPolicies.ManageUsers)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
public class UsersController : ControllerBase
{
    private const string ProblemJson = "application/problem+json";

    private readonly IUserService _users;

    public UsersController(IUserService users)
    {
        _users = users;
    }

    /// <summary>The roles an account can be assigned, for the administrator's role picker.</summary>
    /// <response code="200">The six configured roles.</response>
    [HttpGet("roles")]
    [ProducesResponseType(typeof(IReadOnlyList<string>), StatusCodes.Status200OK, "application/json")]
    public ActionResult<IReadOnlyList<string>> Roles() => Ok(WonrichRoles.All);

    /// <summary>Lists accounts, searchable by name and filterable by role.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="search">Fragment matched against username and display name.</param>
    /// <param name="role">Restrict to accounts holding this role.</param>
    /// <param name="activeOnly">Exclude deactivated accounts.</param>
    /// <response code="200">The matching accounts.</response>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<UserView>), StatusCodes.Status200OK, "application/json")]
    public async Task<ActionResult<IReadOnlyList<UserView>>> List(
        CancellationToken cancellationToken,
        [FromQuery] string? search = null,
        [FromQuery] string? role = null,
        [FromQuery] bool activeOnly = false)
    {
        var query = new UserQuery { Search = search, Role = role, ActiveOnly = activeOnly };

        return Ok(await _users.ListAsync(query, cancellationToken));
    }

    /// <summary>Fetches a single account.</summary>
    /// <response code="200">The account was found.</response>
    /// <response code="404">No account carries that identifier.</response>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(UserView), StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserView>> Get(Guid id, CancellationToken cancellationToken)
    {
        var user = await _users.GetAsync(id, cancellationToken);

        return user is null ? NotFoundProblem(id) : Ok(user);
    }

    /// <summary>Creates an account.</summary>
    /// <response code="201">The account was created.</response>
    /// <response code="400">The details are incomplete, or the role is not one of the six.</response>
    /// <response code="409">That username is already taken.</response>
    [HttpPost]
    [ProducesResponseType(typeof(UserView), StatusCodes.Status201Created, "application/json")]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest, ProblemJson)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<UserView>> Create(
        [FromBody] CreateUserRequest request,
        CancellationToken cancellationToken)
    {
        var command = new CreateUserCommand(
            request.UserName,
            request.DisplayName,
            request.Password,
            request.Role,
            request.Facility);

        try
        {
            var user = await _users.CreateAsync(command, cancellationToken);

            return CreatedAtAction(nameof(Get), new { id = user.Id }, user);
        }
        catch (DuplicateUserNameException exception)
        {
            return Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Username already taken",
                detail: exception.Message);
        }
        catch (ArgumentException exception)
        {
            // An unconfigured role or a blank field: the caller's mistake, not a server fault.
            return RejectedField(exception);
        }
    }

    /// <summary>Amends an account. The username itself cannot be changed.</summary>
    /// <response code="200">The account was updated.</response>
    /// <response code="400">The details are invalid, or the role is not one of the six.</response>
    /// <response code="404">No account carries that identifier.</response>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(UserView), StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest, ProblemJson)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserView>> Update(
        Guid id,
        [FromBody] UpdateUserRequest request,
        CancellationToken cancellationToken)
    {
        var command = new UpdateUserCommand(
            request.DisplayName,
            request.Role,
            request.Facility,
            request.NewPassword);

        return await Scoped(id, () => _users.UpdateAsync(id, command, cancellationToken));
    }

    /// <summary>
    /// Deactivates an account. It can no longer sign in, and any refresh token it already holds
    /// is revoked, but it stays visible so history keeps resolving to it.
    /// </summary>
    /// <response code="200">The account was deactivated.</response>
    /// <response code="404">No account carries that identifier.</response>
    [HttpPost("{id:guid}/deactivate")]
    [ProducesResponseType(typeof(UserView), StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserView>> Deactivate(Guid id, CancellationToken cancellationToken) =>
        await Scoped(id, () => _users.DeactivateAsync(id, cancellationToken));

    /// <summary>Returns a deactivated account to service.</summary>
    /// <response code="200">The account is active again.</response>
    /// <response code="404">No account carries that identifier.</response>
    [HttpPost("{id:guid}/reactivate")]
    [ProducesResponseType(typeof(UserView), StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserView>> Reactivate(Guid id, CancellationToken cancellationToken) =>
        await Scoped(id, () => _users.ReactivateAsync(id, cancellationToken));

    private async Task<ActionResult<UserView>> Scoped(Guid id, Func<Task<UserView>> action)
    {
        try
        {
            return Ok(await action());
        }
        catch (UserNotFoundException)
        {
            return NotFoundProblem(id);
        }
        catch (ArgumentException exception)
        {
            return RejectedField(exception);
        }
    }

    /// <summary>
    /// Reports a field the domain rejected — an unconfigured role, a blank name — in the same
    /// shape [ApiController] produces for model validation, which is what these endpoints
    /// document. Problem(...) returned a bare ProblemDetails instead, so a client reading
    /// <c>errors</c> found the field for a missing role but nothing for an invalid one.
    /// </summary>
    private ActionResult<UserView> RejectedField(ArgumentException exception)
    {
        // ArgumentException appends " (Parameter 'role')" to its message; the field is already
        // the key it is filed under, so the suffix would only repeat it back.
        var message = exception.ParamName is null
            ? exception.Message
            : exception.Message.Replace($" (Parameter '{exception.ParamName}')", string.Empty);

        ModelState.AddModelError(exception.ParamName ?? string.Empty, message);

        return ValidationProblem(ModelState);
    }

    private ObjectResult NotFoundProblem(Guid id) => Problem(
        statusCode: StatusCodes.Status404NotFound,
        title: "Account not found",
        detail: $"No account is registered under identifier '{id}'.");
}
