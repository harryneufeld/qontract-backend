namespace Qontract.Application.Services;

public interface IAuthService
{
    Task<AuthDTO> LoginAsync(LoginDTO dto, CancellationToken cancellationToken = default);
    Task<AuthDTO> RefreshTokenAsync(string refreshToken, CancellationToken cancellationToken = default);
}

public class AuthDTO
{
    public string AccessToken { get; set; } = string.Empty;
    public int UserId { get; set; }
    public string? Email { get; set; }
    public DateTime ExpiresAt { get; set; }
}

public class LoginDTO
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public interface IUserService
{
    Task<List<UserDTO>> GetUsersAsync(int tenantId, CancellationToken cancellationToken = default);
    Task<UserDTO> GetUserByIdAsync(int id, int tenantId, CancellationToken cancellationToken = default);
    Task<UserDTO> CreateUserAsync(UserCreateDTO dto, string? createdBy, CancellationToken cancellationToken = default);
    Task<UserDTO> UpdateUserAsync(int id, int tenantId, UserUpdateDTO dto, string? modifiedBy, CancellationToken cancellationToken = default);
    Task<UserDTO> DeactivateUserAsync(int id, int tenantId, string? modifiedBy, CancellationToken cancellationToken = default);
}
