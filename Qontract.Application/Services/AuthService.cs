using System.Security.Cryptography;
using System.Text;

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

    public async Task<AuthDTO> LoginAsync(LoginDTO dto, CancellationToken cancellationToken = default)
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

            return new AuthDTO
            {
                AccessToken = token,
                UserId = user.Id,
                Email = user.Email,
                ExpiresAt = DateTime.UtcNow.AddHours(24)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during login for email: {Email}", dto.Email);
            throw;
        }
    }

    public async Task<AuthDTO> RefreshTokenAsync(string refreshToken, CancellationToken cancellationToken = default)
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

        return new AuthDTO
        {
            AccessToken = token,
            UserId = user.Id,
            Email = user.Email,
            ExpiresAt = DateTime.UtcNow.AddHours(24)
        };
    }

    private bool VerifyPassword(string password, string passwordHash)
    {
        if (!passwordHash.Contains(SaltKey))
            return false;

        var hashParts = passwordHash.Split(':');
        if (hashParts.Length < 2)
            return false;

        try
        {
            var salt = Convert.FromBase64String(hashParts[0]);
            var hashedPassword = HashPassword(password, salt);

            return hashedPassword == hashParts[1];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying password for email: {Email}", password);
            return false;
        }
    }

    private async Task<string> HashPasswordAsync(string password)
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

    private string HashPassword(string password, byte[] salt)
    {
        using var hmac = new HMACSHA256(salt);
        var hashed = hmac.ComputeHash(Encoding.UTF8.GetBytes(password));
        return Convert.ToBase64String(hashed);
    }

    private byte[] GenerateSalt()
    {
        using var rng = RandomNumberGenerator.Create();
        var salt = new byte[SaltBytesSize];
        rng.GetBytes(salt);
        return salt;
    }

    private string GenerateJwtToken(User user)
    {
        // Generate a sample JWT token
        // In production, use a proper JWT library like System.IdentityModel.Tokens.Jwt

        var keyBytes = System.Text.Encoding.ASCII.GetBytes(_configuration["Jwt:Key"]!);
        var key = new System.Security.Cryptography.SecurityKey
        {
            Key = keyBytes,
            KeySize = 256
        };

        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _configuration["Jwt:Issuer"],
            audience: _configuration["Jwt:Audience"],
            subject: new ClaimsIdentity(new[]
            {
                new Claim("userId", user.Id.ToString()),
                new Claim("email", user.Email),
                new Claim("role", user.Role.ToString())
            }, "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name")
            , expiration: DateTime.UtcNow.AddHours(24),
            notBefore: DateTime.UtcNow,
            issuer: _configuration["Jwt:Issuer"],
            signingCredentials: credentials
        );

        var jwtHandler = new JwtSecurityTokenHandler();
        var tokenString = jwtHandler.WriteToken(token);

        _logger.LogInformation("Generated JWT token for user: {Email}", user.Email);
        return tokenString;
    }
}
