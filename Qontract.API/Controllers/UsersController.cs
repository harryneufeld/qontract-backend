using Qontract.Domain.Models;
using Qontract.Application.Models;
using Qontract.Application.Services;

namespace Qontract.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class UsersController : ControllerBase
{
    private readonly IUserService _userService;

    public UsersController(IUserService userService)
    {
        _userService = userService;
    }

    [HttpGet]
    public async Task<ActionResult<List<UserDTO>>> GetUsers()
    {
        var users = await _userService.GetUsersAsync(1); // TODO: get from auth context
        return Ok(users);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<UserDTO>> GetUser(int id)
    {
        var user = await _userService.GetUserByIdAsync(id, 1);
        return Ok(user);
    }

    [HttpPost]
    public async Task<ActionResult<UserDTO>> CreateUser([FromBody] UserCreateDTO dto)
    {
        var user = await _userService.CreateUserAsync(dto, User.Identity?.Name);
        return CreatedAtAction(nameof(GetUser), new { id = user.Id }, user);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<UserDTO>> UpdateUser(int id, [FromBody] UserUpdateDTO dto)
    {
        var user = await _userService.UpdateUserAsync(id, 1, dto, User.Identity?.Name);
        return Ok(user);
    }

    [HttpPatch("{id}/deactivate")]
    public async Task<ActionResult<UserDTO>> DeactivateUser(int id)
    {
        var user = await _userService.DeactivateUserAsync(id, 1, User.Identity?.Name);
        return Ok(user);
    }
}
