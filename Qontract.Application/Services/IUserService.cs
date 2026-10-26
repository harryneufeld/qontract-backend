using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Qontract.Application.Models;

namespace Qontract.Application.Services;

public interface IAuthService
{
    Task<TwoFactorStatusDTO> TwoFactorStatusAsync(int userId, CancellationToken ct = default);
    Task<TwoFactorSetupDTO> SetupTwoFactorAsync(int userId, string password, CancellationToken ct = default);
    Task<string[]> EnableTwoFactorAsync(int userId, TwoFactorProofDTO proof, CancellationToken ct = default);
    Task DisableTwoFactorAsync(int userId, TwoFactorProofDTO proof, CancellationToken ct = default);
    Task<Qontract.Application.Models.AuthDTO> LoginAsync(Qontract.Application.Models.LoginDTO dto, CancellationToken cancellationToken = default, string? userAgent = null);
    Task<string> HashPasswordAsync(string password);
    Task<Qontract.Application.Models.AuthUserDTO?> GetCurrentUserAsync(int userId, int tenantId, CancellationToken cancellationToken = default);
    Task<AuthDTO> StartConfirmedSessionAsync(int userId, CancellationToken cancellationToken = default, SessionClientInfo? client = null);
    Task<Qontract.Application.Models.AuthUserDTO?> UpdateProfileAsync(int userId, int tenantId, Qontract.Application.Models.ProfileUpdateDTO dto, CancellationToken cancellationToken = default);
    Task<AuthDTO> SwitchTenantAsync(int userId, int tenantId, CancellationToken cancellationToken = default, Guid? sessionId = null, SessionClientInfo? client = null);
    Task LogoutAsync(int userId, Guid sessionId, CancellationToken cancellationToken = default);
    Task<AuthDTO?> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default);
    Task<List<SessionInfoDTO>> ListSessionsAsync(int userId, Guid currentSessionId, CancellationToken cancellationToken = default);
    Task<bool> RevokeSessionAsync(int userId, Guid sessionId, CancellationToken cancellationToken = default);
    Task<int> RevokeOtherSessionsAsync(int userId, Guid currentSessionId, CancellationToken cancellationToken = default);
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
