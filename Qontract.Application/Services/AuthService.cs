using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Qontract.Application.Interfaces;
using Qontract.Application.Models;
using Qontract.Core.Security;
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

    public async Task<string> HashPasswordAsync(string password)
    {
        try
        {
            byte[] salt;
            var hashParts = password.Split(':');

            if (IsSaltedHash(hashParts))
            {
                // Already a stored "salt:hash" pair — reuse the original salt
                // so re-hashing stays stable against the existing stored hash.
                salt = Convert.FromBase64String(hashParts[0]);
            }
            else
            {
                // Plain (unhashed) password: generate a fresh salt. This is the
                // normal case — DTOs carry a plain password, never a hash.
                // (issue #40: a plain password such as "password123" is not
                // base64, so the old "extract salt" branch threw FormatException
                // and 500'd; it is now guarded by IsSaltedHash.)
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

    /// <summary>
    /// True only when the value is a stored "salt:hash" pair — exactly two
    /// ':'-separated segments, both valid base64. Any plain password (no colon,
    /// or a segment that is not base64) is treated as unhashed input and gets a
    /// fresh salt instead of failing to decode (issue #40).
    /// </summary>
    private static bool IsSaltedHash(string[] hashParts)
    {
        return hashParts.Length == 2
            && TryBase64Decode(hashParts[0], out _)
            && TryBase64Decode(hashParts[1], out _);
    }

    private static bool TryBase64Decode(string value, out byte[] decoded)
    {
        try
        {
            decoded = Convert.FromBase64String(value);
            return true;
        }
        catch (FormatException)
        {
            decoded = Array.Empty<byte>();
            return false;
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
        // (issue #36) The development fallback key is the single shared
        // constant Qontract.Core.Security.JwtKeys.DevelopmentFallback, also
        // referenced by the validation pipeline (Qontract.API/Program.cs), so
        // tokens signed here are always accepted by that pipeline.
        var jwtKey = _configuration["Jwt:Key"];
        if (string.IsNullOrWhiteSpace(jwtKey))
        {
            jwtKey = JwtKeys.DevelopmentFallback;
            _logger.LogWarning(
                "Jwt:Key is not configured. Using the development fallback signing key. " +
                "Set 'Jwt:Key' in configuration before deploying.");
        }
        var securityKey = new SymmetricSecurityKey(Encoding.ASCII.GetBytes(jwtKey));
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
            issuer: _configuration["Jwt:Issuer"] ?? "Qontract",
            audience: _configuration["Jwt:Audience"] ?? "QontractClients",
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