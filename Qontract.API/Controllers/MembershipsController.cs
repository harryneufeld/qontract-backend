using Microsoft.AspNetCore.Authorization;
using Qontract.Application.Models;
using Qontract.Application.Services;

namespace Qontract.API.Controllers;

[ApiController]
[Authorize(Roles = AppRoles.TenantAdmin)]
[ApiVersion("1.0")]
[Route("api/tenants/{tenantId:int}")]
[Route("api/v{version:apiVersion}/tenants/{tenantId:int}")]
public class MembershipsController(MembershipService memberships, IUserService users) : ControllerBase
{
    private int UserId => int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
    private string? Actor => User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value;

    [HttpGet("members")]
    public async Task<ActionResult<List<UserDTO>>> Members(int tenantId)
    {
        await memberships.RequireAdmin(UserId, tenantId);
        return Ok(await users.GetUsersAsync(tenantId));
    }

    [HttpPatch("members/{memberId:int}")]
    public async Task<ActionResult<UserDTO>> UpdateMember(int tenantId, int memberId, MembershipUpdateDTO dto)
    {
        await memberships.RequireAdmin(UserId, tenantId);
        return Ok(await users.UpdateUserAsync(memberId, tenantId,
            new UserUpdateDTO { Role = dto.Role, IsActive = dto.IsActive }, Actor));
    }

    [HttpDelete("members/{memberId:int}")]
    public async Task<ActionResult<UserDTO>> RemoveMember(int tenantId, int memberId)
    {
        await memberships.RequireAdmin(UserId, tenantId);
        return Ok(await users.DeactivateUserAsync(memberId, tenantId, Actor));
    }

    [HttpGet("invitations")]
    public async Task<ActionResult<List<InvitationDTO>>> Invitations(int tenantId) =>
        Ok(await memberships.InvitationsAsync(UserId, tenantId));

    [HttpPost("invitations")]
    public async Task<ActionResult<InvitationDTO>> Invite(int tenantId, InvitationCreateDTO dto) =>
        Ok(await memberships.InviteAsync(UserId, tenantId, dto, Actor));

    [HttpDelete("invitations/{invitationId:guid}")]
    public async Task<IActionResult> Revoke(int tenantId, Guid invitationId)
    {
        await memberships.RevokeAsync(UserId, tenantId, invitationId, Actor);
        return NoContent();
    }
}
