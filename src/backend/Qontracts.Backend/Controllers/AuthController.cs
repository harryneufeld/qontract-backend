using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Qontracts.Backend.DTOs;
using Qontracts.Backend.Services;

namespace Qontracts.Backend.Controllers
{
    [ApiController]
    [Route("api/v{version:apiVersion}/[controller]")]
    [ApiVersion("1.0")]
    public class AuthController : ControllerBase
    {
        private readonly IAuthService _authService;

        public AuthController(IAuthService authService)
        {
            _authService = authService;
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] UserRegisterDto userRegisterDto)
        {
            var (success, message) = await _authService.Register(userRegisterDto);
            if (!success)
            {
                return BadRequest(new { message });
            }
            return StatusCode(201, new { message });
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] UserLoginDto userLoginDto)
        {
            var (success, message, response) = await _authService.Login(userLoginDto);
            if (!success)
            {
                return Unauthorized(new { message });
            }
            return Ok(response);
        }

        [HttpPost("refresh-token")]
        public async Task<IActionResult> RefreshToken([FromBody] RefreshTokenDto refreshTokenDto)
        {
            var (success, message, response) = await _authService.RefreshToken(refreshTokenDto);
            if (!success)
            {
                return Unauthorized(new { message });
            }
            return Ok(response);
        }

        [HttpGet("profile")]
        [Authorize]
        public async Task<IActionResult> GetProfile()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (Guid.TryParse(userIdClaim, out var userId))
            {
                var (success, message, profile) = await _authService.GetProfile(userId);
                if (!success)
                {
                    return NotFound(new { message });
                }
                return Ok(profile);
            }

            return Unauthorized(new { message = "Invalid token." });
        }

        [HttpPut("update-password")]
        [Authorize]
        public async Task<IActionResult> UpdatePassword([FromBody] UpdatePasswordDto updatePasswordDto)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (Guid.TryParse(userIdClaim, out var userId))
            {
                var (success, message) = await _authService.UpdatePassword(userId, updatePasswordDto);
                if (!success)
                {
                    return BadRequest(new { message });
                }
                return Ok(new { message });
            }

            return Unauthorized(new { message = "Invalid token." });
        }

        [HttpPut("change-password")]
        [Authorize]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordDto changePasswordDto)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (Guid.TryParse(userIdClaim, out var userId))
            {
                var (success, message) = await _authService.ChangePassword(userId, changePasswordDto);
                if (!success)
                {
                    return BadRequest(new { message });
                }
                return Ok(new { message });
            }

            return Unauthorized(new { message = "Invalid token." });
        }

        [HttpPost("forgot-password")]
        public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordDto forgotPasswordDto)
        {
            var (success, message) = await _authService.ForgotPassword(forgotPasswordDto.Email);
            return Ok(new { message });
        }

        [HttpPost("reset-password")]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordDto resetPasswordDto)
        {
            var (success, message) = await _authService.ResetPassword(resetPasswordDto.Token, resetPasswordDto.NewPassword);
            if (!success)
            {
                return BadRequest(new { message });
            }
            return Ok(new { message });
        }

        [HttpPost("logout")]
        [Authorize]
        public async Task<IActionResult> Logout()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (Guid.TryParse(userIdClaim, out var userId))
            {
                await _authService.RevokeAllUserTokens(userId);
                return Ok(new { message = "Logged out successfully." });
            }

            return Unauthorized(new { message = "Invalid token." });
        }

        [HttpPost("logout-all")]
        [Authorize]
        public async Task<IActionResult> LogoutAllDevices()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (Guid.TryParse(userIdClaim, out var userId))
            {
                await _authService.RevokeAllUserTokens(userId);
                return Ok(new { message = "All devices logged out successfully." });
            }

            return Unauthorized(new { message = "Invalid token." });
        }

        [HttpGet("me")]
        [Authorize]
        public async Task<IActionResult> GetMe()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (Guid.TryParse(userIdClaim, out var userId))
            {
                var (success, message, profile) = await _authService.GetProfile(userId);
                if (!success)
                {
                    return NotFound(new { message });
                }
                return Ok(profile);
            }

            return Unauthorized(new { message = "Invalid token." });
        }
    }
}
