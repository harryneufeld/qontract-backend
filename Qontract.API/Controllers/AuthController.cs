using System.Collections.Generic;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Qontract.Application.Services;
using Qontract.Application.Models;

namespace Qontract.API.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly MembershipService _memberships;
    private readonly IValidator<ChangePasswordDTO> _changePasswordValidator;

    public AuthController(
        IAuthService authService,
        IValidator<ChangePasswordDTO> changePasswordValidator,
        MembershipService memberships)
    {
        _authService = authService;
        _changePasswordValidator = changePasswordValidator;
        _memberships = memberships;
    }

    // [EnableRateLimiting("auth")] (issue #27): the "auth" policy applies a
    // strict per-client-IP ceiling to credential endpoints so a brute-force /
    // credential-stuffing attempt gets a 429 + Retry-After after a few tries
    // instead of an unbounded number of password guesses.
    [HttpPost("login")]
    [EnableRateLimiting("auth")]
    public async Task<ActionResult<Qontract.Application.Models.AuthDTO>> Login([FromBody] Qontract.Application.Models.LoginDTO dto)
    {
        try
        {
            var result = await _authService.LoginAsync(dto);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return Unauthorized(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Returns the currently authenticated user (identity from the JWT Subject claim).
    /// </summary>
    [HttpGet("me")]
    [Authorize(Policy = "Session")]
    public async Task<ActionResult<Qontract.Application.Models.AuthUserDTO>> Me()
    {
        var subject = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value;

        if (string.IsNullOrEmpty(subject) || !int.TryParse(subject, out var userId))
        {
            return Unauthorized(new { error = "Invalid token subject." });
        }

        if (!int.TryParse(User.FindFirst("TenantId")?.Value, out var tenantId)) return Unauthorized();
        var user = await _authService.GetCurrentUserAsync(userId, tenantId);
        return user is null ? Unauthorized(new { error = "User no longer active." }) : Ok(user);
    }

    /// <summary>
    /// Revoke this session, including tokens issued during tenant switches.
    /// </summary>
    [HttpPost("logout")]
    [Authorize(Policy = "Session")]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        await _authService.LogoutAsync(CurrentUserId, CurrentSessionId, cancellationToken);
        return Ok(new { message = "Logged out" });
    }

    private int CurrentUserId => int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
    private Guid CurrentSessionId => Guid.Parse(User.FindFirst("sid")!.Value);

    [HttpGet("registration")]
    public IActionResult RegistrationPolicy() => Ok(new { allowPublic = _memberships.AllowPublicRegistration });

    [HttpPost("register")]
    [EnableRateLimiting("auth")]
    public async Task<ActionResult<AuthDTO>> Register(RegisterDTO dto)
    {
        var access = await _memberships.RegisterAsync(dto);
        return Ok(await _authService.SwitchTenantAsync(access.UserId, access.TenantId));
    }

    [HttpGet("memberships")]
    [Authorize(Policy = "Session")]
    public async Task<ActionResult<List<MembershipDTO>>> Memberships() =>
        Ok(await _memberships.ListAsync(CurrentUserId));

    [HttpPost("switch-tenant")]
    [Authorize(Policy = "Session")]
    public async Task<ActionResult<AuthDTO>> SwitchTenant(SwitchTenantDTO dto) =>
        Ok(await _authService.SwitchTenantAsync(CurrentUserId, dto.TenantId, sessionId: CurrentSessionId));

    [HttpPost("invitations/redeem")]
    [Authorize(Policy = "Session")]
    [EnableRateLimiting("auth")]
    public async Task<ActionResult<AuthDTO>> Redeem(InvitationRedeemDTO dto)
    {
        var tenantId = await _memberships.RedeemAsync(CurrentUserId, dto.Code);
        return Ok(await _authService.SwitchTenantAsync(CurrentUserId, tenantId, sessionId: CurrentSessionId));
    }

    /// <summary>
    /// Self-service password change for the authenticated user (app#11).
    /// A wrong current password yields 400 with a generic message — the
    /// client is already authenticated, so the endpoint never reveals
    /// anything about the stored hash.
    /// </summary>
    [HttpPost("change-password")]
    [Authorize(Policy = "Session")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordDTO dto)
    {
        var reject = await DtoValidation.RejectIfInvalidAsync(_changePasswordValidator, dto);
        if (reject is not null)
            return reject;

        var subject = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value;

        if (string.IsNullOrEmpty(subject) || !int.TryParse(subject, out var userId))
        {
            return Unauthorized(new { error = "Invalid token subject." });
        }

        var ok = await _authService.ChangePasswordAsync(userId, dto.CurrentPassword, dto.NewPassword);
        if (ok)
            return Ok(new { message = "Password changed." });

        // Same { errors: { field: [messages] } } shape the Flutter app parses.
        var errors = new Dictionary<string, string[]>
        {
            ["currentPassword"] = new[] { "Current password is incorrect." }
        };
        return BadRequest(new { errors });
    }
}
