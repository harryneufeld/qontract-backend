using System.ComponentModel.DataAnnotations;
using System.Data.Common;
using Microsoft.AspNetCore.RateLimiting;
using Qontract.API.Services;
using Qontract.Application.Services;

namespace Qontract.API.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/auth")]
[Route("api/auth")]
[EnableRateLimiting("auth")]
public class PasswordResetController(PasswordResetQueue queue, PasswordResetService resets, IConfiguration config) : ControllerBase
{
    [HttpPost("forgot-password")]
    public IActionResult RequestReset(RequestPasswordReset dto)
    {
        if (!PasswordResetService.Enabled(config))
            return StatusCode(503, new { message = "Password reset is not available. Please contact the administrator." });
        // Same response path for every address: no account query or SMTP here.
        if (!queue.TryEnqueue(dto.Email.Trim(), Qontract.API.Localization.UserMessages.Language))
            return StatusCode(503, new { message = "Please try again later." });
        return Accepted(new { message = "If this account is eligible, a password reset link will be sent." });
    }

    [HttpPost("reset-password")]
    public async Task<IActionResult> Reset(CompletePasswordReset dto, CancellationToken cancellationToken)
    {
        if (!PasswordResetService.Enabled(config))
            return StatusCode(503, new { message = "Password reset is not available. Please contact the administrator." });
        try
        {
            if (!await resets.RedeemAsync(dto.Token, dto.NewPassword, cancellationToken))
                return BadRequest(new { message = "This link is invalid or expired. Please request a new link." });
        }
        catch (DbException)
        {
            // Concurrent redemption/serialization failures cannot authorize reset.
            return Conflict(new { message = "Unable to complete reset. Please retry or request a new link." });
        }
        return Ok(new { message = "Password updated. Please sign in again." });
    }
}
public class RequestPasswordReset
{
    [Required, EmailAddress, MaxLength(100)]
    public string Email { get; set; } = string.Empty;
}
public class CompletePasswordReset
{
    [Required, MaxLength(64)]
    public string Token { get; set; } = string.Empty;
    [Required, MinLength(6), MaxLength(255)]
    public string NewPassword { get; set; } = string.Empty;
}
