using Qontracts.Backend.Models;
using Qontracts.Backend.DTOs;

namespace Qontracts.Backend.Services
{
    public interface IAuthService
    {
        Task<(bool Success, string Message)> Register(UserRegisterDto userRegisterDto);
        Task<(bool Success, string Message, AuthResponseDto? Response)> Login(UserLoginDto userLoginDto);
        Task<(bool Success, string Message, AuthResponseDto? Response)> RefreshToken(RefreshTokenDto refreshTokenDto);
        string GenerateAccessToken(User user);
        string GenerateRefreshToken();
        void CreatePasswordHash(string password, out byte[] passwordHash, out byte[] passwordSalt);
        bool VerifyPasswordHash(string password, byte[] passwordHash, byte[] passwordSalt);
    }
}
