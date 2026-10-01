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
                .FirstOrDefaultAsync(u => u.NormalizedEmail == dto.Email.Trim().ToUpperInvariant() && u.IsActive, cancellationToken);

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

            var membership = await _context.TenantMemberships
                .Where(m => m.UserId == user.Id && m.IsActive && m.Tenant.IsActive)
                .OrderByDescending(m => m.TenantId == user.TenantId).ThenBy(m => m.TenantId)
                .FirstOrDefaultAsync(cancellationToken)
                ?? throw new InvalidOperationException("No active tenant membership.");
            return Session(user, membership);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during login for email: {Email}", dto.Email);
            throw;
        }
    }

    public async Task<AuthUserDTO?> GetCurrentUserAsync(int userId, int tenantId, CancellationToken cancellationToken = default)
    {
        var membership = await ActiveMembership(userId, tenantId).FirstOrDefaultAsync(cancellationToken);
        return membership == null ? null : ToAuthUser(membership.User, membership);
    }

    private IQueryable<TenantMembership> ActiveMembership(int userId, int tenantId) =>
        _context.TenantMemberships.Include(m => m.User).Where(m => m.UserId == userId && m.TenantId == tenantId
            && m.IsActive && m.User.IsActive && m.Tenant.IsActive);

    public async Task<AuthDTO> SwitchTenantAsync(int userId, int tenantId, CancellationToken cancellationToken = default)
    {
        var membership = await ActiveMembership(userId, tenantId).FirstOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException("Tenant not found.");
        return Session(membership.User, membership);
    }

    private AuthDTO Session(User user, TenantMembership membership) => new()
    {
        AccessToken = GenerateJwtToken(user, membership), UserId = user.Id, Email = user.Email,
        ExpiresAt = DateTime.UtcNow.AddHours(24), User = ToAuthUser(user, membership)
    };

    /// <summary>
    /// Self-service password change (app#11). Verifies the current password
    /// against the stored hash and — only on a match — stores the hash of the
    /// new password. A wrong current password (or a missing user) returns
    /// false without revealing which check failed.
    /// </summary>
    public async Task<bool> ChangePasswordAsync(int userId, string currentPassword, string newPassword, CancellationToken cancellationToken = default)
    {
        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Id == userId && u.IsActive, cancellationToken);

        if (user == null)
        {
            _logger.LogWarning("Password change for unknown or inactive user {UserId}", userId);
            return false;
        }

        if (!VerifyPassword(currentPassword, user.PasswordHash))
        {
            _logger.LogWarning("Password change rejected for user {UserId}: current password mismatch", userId);
            return false;
        }

        user.PasswordHash = await HashPasswordAsync(newPassword);
        user.ModifiedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Password changed for user {UserId}", userId);
        return true;
    }

    private static AuthUserDTO ToAuthUser(User user, TenantMembership membership) => new()
    {
        Id = user.Id,
        Name = user.Name,
        Email = user.Email,
        Role = membership.Role.ToString(),
        TenantId = membership.TenantId,
        CreatedAt = user.CreatedAt
    };

    public Task<string> HashPasswordAsync(string password) => Task.FromResult(Passwords.Hash(password));

    private bool VerifyPassword(string password, string storedHash)
    {
        if (storedHash.StartsWith("pbkdf2-sha512:", StringComparison.Ordinal))
            return Passwords.Verify(password, storedHash);
        try
        {
            var hashParts = storedHash.Split(':');
            if (hashParts.Length != 2)
            {
                return false;
            }

            var salt = Convert.FromBase64String(hashParts[0]);
            var hash = Convert.FromBase64String(hashParts[1]);

            var inputHash = Convert.FromBase64String(LegacyHashPassword(password, salt));

            return CryptographicOperations.FixedTimeEquals(hash, inputHash);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying stored password format");
            return false;
        }
    }

    // Preserve the original byte layout solely for existing stored hashes.
    private string LegacyHashPassword(string password, byte[] salt)
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

    private string GenerateJwtToken(User user, TenantMembership membership)
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
            new Claim(ClaimTypes.Role, membership.Role.ToString()),
            new Claim("TenantId", membership.TenantId.ToString())
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
