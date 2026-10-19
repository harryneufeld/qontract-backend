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
public class EmailConfirmationController(PasswordResetQueue queue, EmailConfirmationService confirmations, IConfiguration config) : ControllerBase
{
    [HttpPost("resend-confirmation")]
    public IActionResult Resend(RequestEmailConfirmation dto)
    {
        if (!EmailConfirmationService.Required(config))
            return StatusCode(503, new { message = "Email confirmation is not enabled." });
        // Same response path for every address: no account query or SMTP here.
        if (!queue.TryEnqueue(dto.Email.Trim(), Qontract.API.Localization.UserMessages.Language, AccountEmailKind.EmailConfirmation))
            return StatusCode(503, new { message = "Please try again later." });
        return Accepted(new { message = "If this account still needs confirmation, a new link will be sent." });
    }

    [HttpPost("confirm-email")]
    public async Task<IActionResult> Confirm(CompleteEmailConfirmation dto, CancellationToken cancellationToken)
    {
        try
        {
            if (!await confirmations.ConfirmAsync(dto.Token, cancellationToken))
                return BadRequest(new { message = "This link is invalid or expired. Please request a new link." });
        }
        catch (DbException)
        {
            return Conflict(new { message = "Unable to confirm your email address. Please retry or request a new link." });
        }
        return Ok(new { message = "Email address confirmed. You can now sign in." });
    }
}
public class RequestEmailConfirmation
{
    [Required, EmailAddress, MaxLength(100)]
    public string Email { get; set; } = string.Empty;
}
public class CompleteEmailConfirmation
{
    [Required, MaxLength(64)]
    public string Token { get; set; } = string.Empty;
}
