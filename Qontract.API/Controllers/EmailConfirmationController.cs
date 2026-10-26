using System.ComponentModel.DataAnnotations;
using System.Data.Common;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Qontract.Application.Models;
using Qontract.API.Services;
using Qontract.Application.Services;

namespace Qontract.API.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/auth")]
[Route("api/auth")]
[EnableRateLimiting("auth")]
public class EmailConfirmationController(PasswordResetQueue queue, EmailConfirmationService confirmations, IAuthService auth, IConfiguration config,
    IOptions<AuthSessionOptions> sessionOptions) : ControllerBase
{
    private async Task<AuthDTO> StartSession(int userId, string? client, string? deviceName, CancellationToken cancellationToken)
    {
        var info = new SessionClientInfo(client, deviceName, Request.Headers.UserAgent.ToString());
        var session = await auth.StartConfirmedSessionAsync(userId, cancellationToken, info);
        return SessionCookies.Deliver(Response, session, SessionCookies.IsWeb(client), sessionOptions.Value);
    }

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
            var userId = await confirmations.ConfirmTokenAsync(dto.Token, cancellationToken);
            if (userId is null)
                return BadRequest(new { message = "This link is invalid or expired. Please request a new link." });
            // The link proves the address just like the code does, so the app can continue without a second sign-in.
            return Ok(new { message = "Email address confirmed. You can now sign in.",
                session = await StartSession(userId.Value, dto.Client, dto.DeviceName, cancellationToken) });
        }
        catch (DbException)
        {
            return Conflict(new { message = "Unable to confirm your email address. Please retry or request a new link." });
        }
    }

    [HttpPost("confirm-email-code")]
    public async Task<IActionResult> ConfirmCode(CompleteEmailConfirmationCode dto, CancellationToken cancellationToken)
    {
        if (!EmailConfirmationService.Required(config))
            return StatusCode(503, new { message = "Email confirmation is not enabled." });
        try
        {
            var userId = await confirmations.ConfirmCodeAsync(dto.Email, dto.Code, cancellationToken);
            if (userId is null)
                return BadRequest(new { message = "This code is invalid or expired. Please request a new one." });
            return Ok(new { message = "Email address confirmed.",
                session = await StartSession(userId.Value, dto.Client, dto.DeviceName, cancellationToken) });
        }
        catch (DbException)
        {
            return Conflict(new { message = "Unable to confirm your email address. Please retry or request a new code." });
        }
    }
}
public class RequestEmailConfirmation
{
    [Required, EmailAddress, MaxLength(100)]
    public string Email { get; set; } = string.Empty;
}
public class CompleteEmailConfirmationCode
{
    [Required, EmailAddress, MaxLength(100)]
    public string Email { get; set; } = string.Empty;
    [Required, StringLength(6, MinimumLength = 6)]
    public string Code { get; set; } = string.Empty;
    [MaxLength(16)] public string? Client { get; set; }
    [MaxLength(100)] public string? DeviceName { get; set; }
}
public class CompleteEmailConfirmation
{
    [Required, MaxLength(64)]
    public string Token { get; set; } = string.Empty;
    [MaxLength(16)] public string? Client { get; set; }
    [MaxLength(100)] public string? DeviceName { get; set; }
}
