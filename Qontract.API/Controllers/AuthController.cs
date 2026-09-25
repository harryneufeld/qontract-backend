using System.Collections.Generic;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Qontract.Application.Services;
using Qontract.Application.Models;

namespace Qontract.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly IValidator<ChangePasswordDTO> _changePasswordValidator;

    public AuthController(
        IAuthService authService,
        IValidator<ChangePasswordDTO> changePasswordValidator)
    {
        _authService = authService;
        _changePasswordValidator = changePasswordValidator;
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
    [Authorize]
    public async Task<ActionResult<Qontract.Application.Models.AuthUserDTO>> Me()
    {
        var subject = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value;

        if (string.IsNullOrEmpty(subject) || !int.TryParse(subject, out var userId))
        {
            return Unauthorized(new { error = "Invalid token subject." });
        }

        var user = await _authService.GetCurrentUserAsync(userId);
        return user is null ? Unauthorized(new { error = "User no longer active." }) : Ok(user);
    }

    /// <summary>
    /// Stateless logout — the client discards its token.
    /// </summary>
    [HttpPost("logout")]
    [Authorize]
    public IActionResult Logout()
    {
        return Ok(new { message = "Logged out" });
    }

    /// <summary>
    /// Self-service password change for the authenticated user (app#11).
    /// A wrong current password yields 400 with a generic message — the
    /// client is already authenticated, so the endpoint never reveals
    /// anything about the stored hash.
    /// </summary>
    [HttpPost("change-password")]
    [Authorize]
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