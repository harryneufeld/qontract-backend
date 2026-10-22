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
    private readonly TermsService _terms;
    private readonly Qontract.API.Services.PasswordResetQueue _emails;
    private readonly IConfiguration _configuration;

    public AuthController(
        IAuthService authService,
        IValidator<ChangePasswordDTO> changePasswordValidator,
        MembershipService memberships,
        TermsService terms,
        Qontract.API.Services.PasswordResetQueue emails,
        IConfiguration configuration)
    {
        _authService = authService;
        _changePasswordValidator = changePasswordValidator;
        _memberships = memberships;
        _terms = terms;
        _emails = emails;
        _configuration = configuration;
    }

    // [EnableRateLimiting("auth")] (issue #27): the "auth" policy applies a
    // strict per-client-IP ceiling to credential endpoints so a brute-force /
    // credential-stuffing attempt gets a 429 + Retry-After after a few tries
    // instead of an unbounded number of password guesses.
    [HttpPost("login")]
    [EnableRateLimiting("auth")]
    public async Task<ActionResult<Qontract.Application.Models.AuthDTO>> Login([FromBody] Qontract.Application.Models.LoginDTO dto)
    {
        Response.Headers.CacheControl = "no-store";
        try
        {
            var result = await _authService.LoginAsync(dto);
            return Ok(result);
        }
        catch (EmailNotConfirmedException ex)
        {
            return StatusCode(403, new { error = ex.Message, code = "email_not_confirmed" });
        }
        catch (InvalidOperationException ex)
        {
            return Unauthorized(new { error = ex.Message });
        }
    }

    [HttpGet("two-factor")]
    [Authorize(Policy = "Account")]
    public async Task<ActionResult<TwoFactorStatusDTO>> TwoFactorStatus(CancellationToken ct) =>
        Ok(await _authService.TwoFactorStatusAsync(CurrentUserId, ct));

    [HttpPost("two-factor/{actionName}")]
    [Authorize(Policy = "Account")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> ManageTwoFactor(string actionName, TwoFactorProofDTO proof, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        try
        {
            switch (actionName)
            {
                case "setup": return Ok(await _authService.SetupTwoFactorAsync(CurrentUserId, proof.Password, ct));
                case "enable": return Ok(new { recoveryCodes = await _authService.EnableTwoFactorAsync(CurrentUserId, proof, ct) });
                case "disable":
                    await _authService.DisableTwoFactorAsync(CurrentUserId, proof, ct);
                    return Ok(new { message = "Two-factor authentication disabled. Please sign in again." });
                default: return NotFound();
            }
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
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
    public IActionResult RegistrationPolicy() => Ok(new
    {
        allowPublic = _memberships.AllowPublicRegistration,
        requireEmailConfirmation = EmailConfirmationService.Required(_configuration),
        termsVersion = TermsService.CurrentVersion(_configuration),
        termsUrl = TermsService.Url(_configuration)
    });

    [HttpPost("register")]
    [EnableRateLimiting("auth")]
    public async Task<ActionResult<AuthDTO>> Register(RegisterDTO dto)
    {
        var access = await _memberships.RegisterAsync(dto);
        if (EmailConfirmationService.Required(_configuration))
        {
            // No session until the address is confirmed; delivery runs in the background worker.
            _emails.TryEnqueue(dto.Email.Trim(), Qontract.API.Localization.UserMessages.Language,
                Qontract.API.Services.AccountEmailKind.EmailConfirmation);
            return Accepted(new { confirmationRequired = true,
                message = "Check your inbox and confirm your email address to activate your account." });
        }
        return Ok(await _authService.SwitchTenantAsync(access.UserId, access.TenantId));
    }

    /// <summary>
    /// Current terms of use and whether this account accepted them; Version is null when none are configured.
    /// </summary>
    [HttpGet("terms")]
    [Authorize(Policy = "Account")]
    public async Task<ActionResult<TermsStatusDTO>> Terms(CancellationToken ct) =>
        Ok(await _terms.StatusAsync(CurrentUserId, ct));

    [HttpPost("terms/accept")]
    [Authorize(Policy = "Account")]
    public async Task<IActionResult> AcceptTerms(AcceptTermsDTO dto, CancellationToken ct)
    {
        try { await _terms.AcceptAsync(CurrentUserId, dto.Version, ct); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
        return Ok(await _terms.StatusAsync(CurrentUserId, ct));
    }

    /// <summary>
    /// First-run setup and later preference changes: name, default currency, language, onboarding done.
    /// </summary>
    [HttpPut("profile")]
    [Authorize(Policy = "Session")]
    public async Task<ActionResult<AuthUserDTO>> UpdateProfile(ProfileUpdateDTO dto)
    {
        if (!int.TryParse(User.FindFirst("TenantId")?.Value, out var tenantId)) return Unauthorized();
        try
        {
            var user = await _authService.UpdateProfileAsync(CurrentUserId, tenantId, dto);
            return user is null ? Unauthorized() : Ok(user);
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
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

public class AcceptTermsDTO
{
    [System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.StringLength(50)]
    public string Version { get; set; } = string.Empty;
}
