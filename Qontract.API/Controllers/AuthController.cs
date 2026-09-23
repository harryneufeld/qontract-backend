using Microsoft.AspNetCore.Authorization;
using Qontract.Application.Services;
using Qontract.Application.Models;

namespace Qontract.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    [HttpPost("login")]
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
}