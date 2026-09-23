using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Qontract.Domain.Models;
using Qontract.Application.Models;
using Qontract.Application.Services;

namespace Qontract.API.Controllers;

[ApiController]
// User management (list, read, create/invite, update, deactivate) is an
// admin-only operation (issue #24): only tenant admins may manage the
// members of their tenant.
[Authorize(Roles = AppRoles.TenantAdmin)]
[Route("api/[controller]")]
public class UsersController : ControllerBase
{
    private readonly IUserService _userService;
    private readonly IValidator<UserCreateDTO> _createValidator;
    private readonly IValidator<UserUpdateDTO> _updateValidator;

    public UsersController(
        IUserService userService,
        IValidator<UserCreateDTO> createValidator,
        IValidator<UserUpdateDTO> updateValidator)
    {
        _userService = userService;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
    }

    /// <summary>
    /// The tenant a request applies to, resolved from the authenticated user's
    /// TenantId claim (same convention as the other controllers).
    /// </summary>
    private int CurrentTenantId =>
        int.Parse(User.FindFirst("TenantId")?.Value
            ?? throw new InvalidOperationException("Authenticated user has no TenantId claim."));

    [HttpGet]
    public async Task<ActionResult<List<UserDTO>>> GetUsers()
    {
        var users = await _userService.GetUsersAsync(CurrentTenantId);
        return Ok(users);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<UserDTO>> GetUser(int id)
    {
        var user = await _userService.GetUserByIdAsync(id, CurrentTenantId);
        return Ok(user);
    }

    [HttpPost]
    public async Task<ActionResult<UserDTO>> CreateUser([FromBody] UserCreateDTO dto)
    {
        var reject = await DtoValidation.RejectIfInvalidAsync(_createValidator, dto);
        if (reject is not null)
            return reject;

        // Always scope to the authenticated tenant, never trust the client
        // (issue #32) — same convention as the contractor controller.
        dto.TenantId = CurrentTenantId;

        var user = await _userService.CreateUserAsync(dto, User.Identity?.Name);
        return CreatedAtAction(nameof(GetUser), new { id = user.Id }, user);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<UserDTO>> UpdateUser(int id, [FromBody] UserUpdateDTO dto)
    {
        var reject = await DtoValidation.RejectIfInvalidAsync(_updateValidator, dto);
        if (reject is not null)
            return reject;

        var user = await _userService.UpdateUserAsync(id, CurrentTenantId, dto, User.Identity?.Name);
        return Ok(user);
    }

    [HttpPatch("{id}/deactivate")]
    public async Task<ActionResult<UserDTO>> DeactivateUser(int id)
    {
        var user = await _userService.DeactivateUserAsync(id, CurrentTenantId, User.Identity?.Name);
        return Ok(user);
    }
}
