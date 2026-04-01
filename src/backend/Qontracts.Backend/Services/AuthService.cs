using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Qontracts.Backend.Data;
using Qontracts.Backend.DTOs;
using Qontracts.Backend.Models;

namespace Qontracts.Backend.Services
{
    public class AuthService : IAuthService
    {
        private readonly ApplicationDbContext _context;
        private readonly IConfiguration _configuration;

        public AuthService(ApplicationDbContext context, IConfiguration configuration)
        {
            _context = context;
            _configuration = configuration;
        }

        public async Task<(bool Success, string Message)> Register(UserRegisterDto userRegisterDto)
        {
            if (await _context.Users.AnyAsync(u => u.Username == userRegisterDto.Username))
            {
                return (false, "Username already exists.");
            }

            if (await _context.Users.AnyAsync(u => u.Email == userRegisterDto.Email))
            {
                return (false, "Email already registered.");
            }

            CreatePasswordHash(userRegisterDto.Password, out byte[] passwordHash, out byte[] passwordSalt);

            var user = new User
            {
                Username = userRegisterDto.Username,
                Email = userRegisterDto.Email,
                PasswordHash = passwordHash,
                PasswordSalt = passwordSalt,
                Role = "User" // Default role
            };

            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            return (true, "Registration successful.");
        }

        public async Task<(bool Success, string Message, AuthResponseDto? Response)> Login(UserLoginDto userLoginDto)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Username == userLoginDto.Username);
            if (user == null)
            {
                return (false, "Invalid credentials.", null);
            }

            if (!VerifyPasswordHash(userLoginDto.Password, user.PasswordHash, user.PasswordSalt))
            {
                return (false, "Invalid credentials.", null);
            }

            var accessToken = GenerateAccessToken(user);
            var refreshToken = GenerateRefreshToken();

            await StoreRefreshToken(user.Id, refreshToken);

            var authResponse = new AuthResponseDto
            {
                UserId = user.Id,
                Username = user.Username,
                Email = user.Email,
                FirstName = user.FirstName,
                LastName = user.LastName,
                AccessToken = accessToken,
                RefreshToken = refreshToken
            };

            return (true, "Login successful.", authResponse);
        }

        public async Task<(bool Success, string Message, AuthResponseDto? Response)> RefreshToken(RefreshTokenDto refreshTokenDto)
        {
            var token = await _context.RefreshTokens
                .Include(rt => rt.User)
                .FirstOrDefaultAsync(rt => rt.Token == refreshTokenDto.RefreshToken);

            if (token == null || !token.IsActive)
            {
                return (false, "Invalid or expired refresh token.", null);
            }

            // Revoke the old token
            token.RevokedAt = DateTime.UtcNow;
            token.IsRevoked = true;

            // Generate new tokens
            var newRefreshToken = GenerateRefreshToken();
            await StoreRefreshToken(token.UserId, newRefreshToken);

            var accessToken = GenerateAccessToken(token.User!);

            var authResponse = new AuthResponseDto
            {
                UserId = token.UserId,
                Username = token.User.Username,
                Email = token.User.Email,
                FirstName = token.User.FirstName,
                LastName = token.User.LastName,
                AccessToken = accessToken,
                RefreshToken = newRefreshToken
            };

            return (true, "Token refreshed successfully.", authResponse);
        }

        public async Task<(bool Success, string Message)> UpdatePassword(Guid userId, UpdatePasswordDto updatePasswordDto)
        {
            var user = await _context.Users.FindAsync(userId);
            if (user == null)
            {
                return (false, "User not found.");
            }

            if (!VerifyPasswordHash(updatePasswordDto.CurrentPassword, user.PasswordHash, user.PasswordSalt))
            {
                return (false, "Current password is incorrect.");
            }

            CreatePasswordHash(updatePasswordDto.NewPassword, out byte[] passwordHash, out byte[] passwordSalt);
            user.PasswordHash = passwordHash;
            user.PasswordSalt = passwordSalt;
            user.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return (true, "Password updated successfully.");
        }

        public async Task<(bool Success, string Message)> ChangePassword(Guid userId, ChangePasswordDto changePasswordDto)
        {
            var user = await _context.Users.FindAsync(userId);
            if (user == null)
            {
                return (false, "User not found.");
            }

            if (changePasswordDto.NewPassword.Length < 8)
            {
                return (false, "Password must be at least 8 characters.");
            }

            CreatePasswordHash(changePasswordDto.NewPassword, out byte[] passwordHash, out byte[] passwordSalt);
            user.PasswordHash = passwordHash;
            user.PasswordSalt = passwordSalt;
            user.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            // Revoke all refresh tokens for this user
            await RevokeAllUserTokens(userId);

            return (true, "Password changed successfully.");
        }

        public async Task<(bool Success, string Message)> ForgotPassword(string email)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email);
            if (user == null)
            {
                return (true, "If the email exists, a password reset link has been sent.");
            }

            // In a real application, you would generate a reset token and send an email
            // For now, just log that a reset would be triggered
            // TODO: Implement email sending

            return (true, "If the email exists, a password reset link has been sent.");
        }

        public async Task<(bool Success, string Message)> ResetPassword(string token, string newPassword)
        {
            if (newPassword.Length < 8)
            {
                return (false, "Password must be at least 8 characters.");
            }

            var resetToken = await _context.RefreshTokens
                .Include(rt => rt.User)
                .FirstOrDefaultAsync(rt => rt.Token == token && rt.IsActive);

            if (resetToken == null)
            {
                return (false, "Invalid or expired reset token.");
            }

            CreatePasswordHash(newPassword, out byte[] passwordHash, out byte[] passwordSalt);
            resetToken.User!.PasswordHash = passwordHash;
            resetToken.User.PasswordSalt = passwordSalt;
            resetToken.User.UpdatedAt = DateTime.UtcNow;

            // Revoke the reset token
            resetToken.RevokedAt = DateTime.UtcNow;
            resetToken.IsRevoked = true;

            await _context.SaveChangesAsync();

            return (true, "Password reset successfully.");
        }

        public async Task<(bool Success, string Message, UserProfileDto? Profile)> GetProfile(Guid userId)
        {
            var user = await _context.Users.FindAsync(userId);
            if (user == null)
            {
                return (false, "User not found.", null);
            }

            var profile = new UserProfileDto
            {
                UserId = user.Id,
                Username = user.Username,
                Email = user.Email,
                FirstName = user.FirstName,
                LastName = user.LastName,
                Role = user.Role,
                CreatedAt = user.CreatedAt
            };

            return (true, "Profile retrieved successfully.", profile);
        }

        public async Task RevokeAllUserTokens(Guid userId)
        {
            var userTokens = await _context.RefreshTokens
                .Where(rt => rt.UserId == userId && rt.IsActive)
                .ToListAsync();

            foreach (var token in userTokens)
            {
                token.RevokedAt = DateTime.UtcNow;
                token.IsRevoked = true;
            }

            await _context.SaveChangesAsync();
        }

        public string GenerateAccessToken(User user)
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.Username),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim(ClaimTypes.Role, user.Role),
                new Claim(ClaimTypes.GivenName, user.FirstName ?? string.Empty),
                new Claim(ClaimTypes.Surname, user.LastName ?? string.Empty)
            };

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_configuration["Jwt:Key"] ?? throw new InvalidOperationException("JWT Key not configured.")));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha512Signature);

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(claims),
                Expires = DateTime.UtcNow.AddMinutes(15), // Access token valid for 15 minutes
                SigningCredentials = creds,
                Issuer = _configuration["Jwt:Issuer"],
                Audience = _configuration["Jwt:Audience"]
            };

            var tokenHandler = new JwtSecurityTokenHandler();
            var token = tokenHandler.CreateToken(tokenDescriptor);
            return tokenHandler.WriteToken(token);
        }

        public async Task StoreRefreshToken(Guid userId, string token)
        {
            var refreshToken = new RefreshToken
            {
                UserId = userId,
                Token = token,
                DeviceInfo = "Web Client", // In production, capture actual device info
                IpAddress = "Unknown", // In production, capture actual IP
                ExpiresAt = DateTime.UtcNow.AddDays(7), // Refresh tokens valid for 7 days
                CreatedAt = DateTime.UtcNow
            };

            await _context.RefreshTokens.AddAsync(refreshToken);
            await _context.SaveChangesAsync();
        }

        public string GenerateRefreshToken()
        {
            var randomNumber = new byte[32];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(randomNumber);
                return Convert.ToBase64String(randomNumber);
            }
        }

        public void CreatePasswordHash(string password, out byte[] passwordHash, out byte[] passwordSalt)
        {
            using (var hmac = new HMACSHA512())
            {
                passwordSalt = hmac.Key;
                passwordHash = hmac.ComputeHash(Encoding.UTF8.GetBytes(password));
            }
        }

        public bool VerifyPasswordHash(string password, byte[] passwordHash, byte[] passwordSalt)
        {
            using (var hmac = new HMACSHA512(passwordSalt))
            {
                var computedHash = hmac.ComputeHash(Encoding.UTF8.GetBytes(password));
                return computedHash.SequenceEqual(passwordHash);
            }
        }
    }
}