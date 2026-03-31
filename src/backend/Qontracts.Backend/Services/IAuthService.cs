using Qontracts.Backend.Models;
using Qontracts.Backend.DTOs;

namespace Qontracts.Backend.Services
{
    public interface IAuthService
    {
        Task<(bool Success, string Message)> Register(UserRegisterDto userRegisterDto);
        Task<(bool Success, string Message, AuthResponseDto? Response)> Login(UserLoginDto userLoginDto);
        Task<(bool Success, string Message, AuthResponseDto? Response)> RefreshToken(RefreshTokenDto refreshTokenDto);
        Task<(bool Success, string Message, UserProfileDto? Profile)> GetProfile(Guid userId);
        Task<(bool Success, string Message)> UpdatePassword(Guid userId, UpdatePasswordDto updatePasswordDto);
        Task<(bool Success, string Message)> ChangePassword(Guid userId, ChangePasswordDto changePasswordDto);
        Task<(bool Success, string Message)> ForgotPassword(string email);
        Task<(bool Success, string Message)> ResetPassword(string token, string newPassword);
        Task RevokeAllUserTokens(Guid userId);
        string GenerateAccessToken(User user);
        string GenerateRefreshToken();
        void CreatePasswordHash(string password, out byte[] passwordHash, out byte[] passwordSalt);
        bool VerifyPasswordHash(string password, byte[] passwordHash, byte[] passwordSalt);
    }
}
