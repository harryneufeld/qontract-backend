using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Qontract.Domain.Models;
using Qontract.Application.Models;
using Qontract.Application.Services;

namespace Qontract.API.Controllers;

[ApiController]
// Member management (list, read, role/activity update, deactivate) is an
// admin-only operation (issue #24): only tenant admins may manage the
// members of their tenant. Members are added through invitations; account
// credentials are never set by an administrator (issue #80).
[Authorize(Roles = AppRoles.TenantAdmin)]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Route("api/[controller]")]
public class UsersController : ControllerBase
{
    private readonly IUserService _userService;
    private readonly IValidator<UserUpdateDTO> _updateValidator;

    public UsersController(IUserService userService, IValidator<UserUpdateDTO> updateValidator)
    {
        _userService = userService;
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
