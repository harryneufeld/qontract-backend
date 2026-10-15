using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Qontract.Application.Models;

namespace Qontract.Application.Services;

public interface IAuthService
{
    Task<Qontract.Application.Models.AuthDTO> LoginAsync(Qontract.Application.Models.LoginDTO dto, CancellationToken cancellationToken = default);
    Task<string> HashPasswordAsync(string password);
    Task<Qontract.Application.Models.AuthUserDTO?> GetCurrentUserAsync(int userId, int tenantId, CancellationToken cancellationToken = default);
    Task<AuthDTO> SwitchTenantAsync(int userId, int tenantId, CancellationToken cancellationToken = default, Guid? sessionId = null);
    Task LogoutAsync(int userId, Guid sessionId, CancellationToken cancellationToken = default);
    /// <summary>
    /// Self-service password change (app#11): verifies <paramref name="currentPassword"/>
    /// against the user's stored hash and, when it matches, replaces it with the hash
    /// of <paramref name="newPassword"/>. Returns true on success.
    /// </summary>
    Task<bool> ChangePasswordAsync(int userId, string currentPassword, string newPassword, CancellationToken cancellationToken = default);
}

public interface IUserService
{
    Task<List<UserDTO>> GetUsersAsync(int tenantId, CancellationToken cancellationToken = default);
    Task<UserDTO> GetUserByIdAsync(int id, int tenantId, CancellationToken cancellationToken = default);
    Task<UserDTO> UpdateUserAsync(int id, int tenantId, UserUpdateDTO dto, string? modifiedBy, CancellationToken cancellationToken = default);
    Task<UserDTO> DeactivateUserAsync(int id, int tenantId, string? modifiedBy, CancellationToken cancellationToken = default);
}
