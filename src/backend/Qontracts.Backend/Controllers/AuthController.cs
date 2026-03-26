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
    }
}
