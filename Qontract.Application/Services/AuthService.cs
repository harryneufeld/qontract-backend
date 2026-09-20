using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Qontract.Application.Interfaces;
using Qontract.Application.Models;
using System.Security.Cryptography;
using System.Text;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;

namespace Qontract.Application.Services;

public class AuthService : IAuthService
{
    private readonly IApplicationDbContext _context;
    private readonly IConfiguration _configuration;
    private const string SaltKey = "salt";
    private const int SaltBytesSize = 16;
    private readonly ILogger<AuthService> _logger;

    public AuthService(IApplicationDbContext context, IConfiguration configuration, ILogger<AuthService> logger)
    {
        _context = context;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<Qontract.Application.Models.AuthDTO> LoginAsync(Qontract.Application.Models.LoginDTO dto, CancellationToken cancellationToken = default)
    {
        try
        {
            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Email == dto.Email && u.IsActive, cancellationToken);

            if (user == null)
            {
                _logger.LogWarning("Invalid login attempt for email: {Email}", dto.Email);
                throw new InvalidOperationException("Invalid email or password");
            }

            if (!VerifyPassword(dto.Password, user.PasswordHash))
            {
                _logger.LogWarning("Invalid login attempt for email: {Email}", dto.Email);
                throw new InvalidOperationException("Invalid email or password");
            }

            var token = GenerateJwtToken(user);

            return new Qontract.Application.Models.AuthDTO
            {
                AccessToken = token,
                UserId = user.Id,
                Email = user.Email,
                ExpiresAt = DateTime.UtcNow.AddHours(24),
                User = ToAuthUser(user)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during login for email: {Email}", dto.Email);
            throw;
        }
    }

    public async Task<AuthUserDTO?> GetCurrentUserAsync(int userId, CancellationToken cancellationToken = default)
    {
        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Id == userId && u.IsActive, cancellationToken);

        return user == null ? null : ToAuthUser(user);
    }

    private static AuthUserDTO ToAuthUser(User user) => new()
    {
        Id = user.Id,
        Name = user.Name,
        Email = user.Email,
        Role = user.Role.ToString(),
        TenantId = user.TenantId,
        CreatedAt = user.CreatedAt
    };

    public async Task<Qontract.Application.Models.AuthDTO> RefreshTokenAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        // In production, validate the refresh token here
        // For now, return a new access token

        // TODO: Implement refresh token validation
        // This would validate the JWT token and issue a new one

        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Email == "refresh@example.com", cancellationToken);

        if (user == null)
        {
            throw new InvalidOperationException("Invalid refresh token");
        }

        var token = GenerateJwtToken(user);

        return new Qontract.Application.Models.AuthDTO
        {
            AccessToken = token,
            UserId = user.Id,
            Email = user.Email,
            ExpiresAt = DateTime.UtcNow.AddHours(24)
        };
    }

    public async Task<string> HashPasswordAsync(string password)
    {
        try
        {
            byte[] salt;
            var hashParts = password.Split(':');

            if (hashParts.Length >= 1 && hashParts[0] != null && hashParts[0] != SaltKey)
            {
                // Existing password hash, extract salt
                salt = Convert.FromBase64String(hashParts[0]);
            }
            else
            {
                // Generate new salt
                salt = GenerateSalt();
            }

            var hashed = HashPassword(password, salt);
            var saltBase64 = Convert.ToBase64String(salt);
            return $"{saltBase64}:{hashed}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error hashing password");
            throw;
        }
    }

    private bool VerifyPassword(string password, string storedHash)
    {
        try
        {
            var hashParts = storedHash.Split(':');
            if (hashParts.Length != 2)
            {
                return false;
            }

            var salt = Convert.FromBase64String(hashParts[0]);
            var hash = hashParts[1];

            var inputHash = HashPassword(password, salt);

            return hash == inputHash;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying password for email: {Email}", password);
            return false;
        }
    }

    private byte[] GenerateSalt()
    {
        var salt = new byte[SaltBytesSize];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(salt);
        }
        return salt;
    }

    private string HashPassword(string password, byte[] salt)
    {
        using (var sha256 = SHA256.Create())
        {
            var combined = new byte[salt.Length + password.Length];
            Buffer.BlockCopy(salt, 0, combined, 0, salt.Length);
            Buffer.BlockCopy(System.Text.Encoding.UTF8.GetBytes(password), 0, combined, salt.Length, password.Length);

            var hash = sha256.ComputeHash(combined);
            return Convert.ToBase64String(hash);
        }
    }

    private string GenerateJwtToken(User user)
    {
        var securityKey = new SymmetricSecurityKey(Encoding.ASCII.GetBytes(_configuration["Jwt:Key"]!));
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email!),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Role, user.Role.ToString()),
            new Claim("TenantId", user.TenantId.ToString())
        };

        var token = new JwtSecurityToken(
            issuer: _configuration["Jwt:Issuer"],
            audience: _configuration["Jwt:Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddHours(24),
            signingCredentials: credentials
        );

        var jwtHandler = new JwtSecurityTokenHandler();
        var tokenString = jwtHandler.WriteToken(token);

        _logger.LogInformation("Generated JWT token for user: {Email}", user.Email);
        return tokenString;
    }
}