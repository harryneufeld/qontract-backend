using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Qontract.Application.Interfaces;
using Qontract.Application.Models;
using Qontract.Core.Security;
using System.Security.Cryptography;
using System.Text;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;

namespace Qontract.Application.Services;

public partial class AuthService : IAuthService
{
    private readonly IApplicationDbContext _context;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AuthService> _logger;
    private readonly ITwoFactorSecretProtector? _twoFactorProtector;
    private readonly AuthSessionOptions _sessionOptions;

    public AuthService(IApplicationDbContext context, IConfiguration configuration, ILogger<AuthService> logger,
        ITwoFactorSecretProtector? twoFactorProtector = null, IOptions<AuthSessionOptions>? sessionOptions = null)
    {
        _sessionOptions = sessionOptions?.Value ?? new AuthSessionOptions();
        _context = context;
        _configuration = configuration;
        _logger = logger;
        _twoFactorProtector = twoFactorProtector;
    }

    public async Task<Qontract.Application.Models.AuthDTO> LoginAsync(Qontract.Application.Models.LoginDTO dto, CancellationToken cancellationToken = default, string? userAgent = null)
    {
        try
        {
            var user = await _context.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.NormalizedEmail == dto.Email.Trim().ToUpperInvariant() && u.IsActive, cancellationToken);

            if (user == null || user.RequiresPasswordSetup)
            {
                // (issue #80) Spend the same PBKDF2 work as a real account so
                // response timing does not reveal which emails are registered.
                Passwords.Verify(dto.Password, DummyPasswordHash);
                _logger.LogWarning("Invalid login attempt for email: {Email}", dto.Email);
                throw new InvalidOperationException("Invalid email or password");
            }

            if (!VerifyPassword(dto.Password, user.PasswordHash))
            {
                _logger.LogWarning("Invalid login attempt for email: {Email}", dto.Email);
                throw new InvalidOperationException("Invalid email or password");
            }

            await UpgradeLegacyHashAsync(user, dto.Password, cancellationToken);
            if (user.EmailConfirmedAt is null && EmailConfirmationService.Required(_configuration))
                throw new EmailNotConfirmedException();
            if (user.TwoFactorEnabled)
            {
                if (string.IsNullOrWhiteSpace(dto.TwoFactorCode)) return new AuthDTO { RequiresTwoFactor = true };
                if (!await ConsumeSecondFactor(user, dto.TwoFactorCode, dto.UseRecoveryCode, cancellationToken))
                    throw new InvalidOperationException("Invalid or already used two-factor code. Try a new code or a recovery code.");
            }

            var membership = await _context.TenantMemberships
                .Where(m => m.UserId == user.Id && m.IsActive && m.Tenant.IsActive)
                .OrderByDescending(m => m.TenantId == user.TenantId).ThenBy(m => m.TenantId)
                .FirstOrDefaultAsync(cancellationToken);
            return await SessionAsync(user, membership, cancellationToken, client: new SessionClientInfo(dto.Client, dto.DeviceName, userAgent));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during login for email: {Email}", dto.Email);
            throw;
        }
    }

    // Starts the first session right after email confirmation (code or link), without a second sign-in.
    public async Task<AuthDTO> StartConfirmedSessionAsync(int userId, CancellationToken cancellationToken = default, SessionClientInfo? client = null)
    {
        var user = await _context.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId && u.IsActive
                && !u.RequiresPasswordSetup && u.EmailConfirmedAt != null, cancellationToken)
            ?? throw new KeyNotFoundException("Account not found.");
        var membership = await _context.TenantMemberships
            .Where(m => m.UserId == user.Id && m.IsActive && m.Tenant.IsActive)
            .OrderByDescending(m => m.TenantId == user.TenantId).ThenBy(m => m.TenantId)
            .FirstOrDefaultAsync(cancellationToken);
        return await SessionAsync(user, membership, cancellationToken, client: client);
    }

    public async Task<AuthUserDTO?> UpdateProfileAsync(int userId, int tenantId, ProfileUpdateDTO dto, CancellationToken cancellationToken = default)
    {
        var user = await _context.Users.SingleOrDefaultAsync(u => u.Id == userId && u.IsActive, cancellationToken);
        if (user is null) return null;
        if (!string.IsNullOrWhiteSpace(dto.Name)) user.Name = dto.Name.Trim();
        if (!string.IsNullOrWhiteSpace(dto.DefaultCurrency)) user.DefaultCurrency = TenantCurrency.Normalize(dto.DefaultCurrency);
        if (!string.IsNullOrWhiteSpace(dto.Language))
            user.Language = Qontract.Application.Localization.SupportedLanguage.Normalize(dto.Language);
        if (dto.OnboardingCompleted == true) user.OnboardingCompletedAt ??= DateTime.UtcNow;
        user.ModifiedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);
        return await GetCurrentUserAsync(userId, tenantId, cancellationToken);
    }

    public async Task<AuthUserDTO?> GetCurrentUserAsync(int userId, int tenantId, CancellationToken cancellationToken = default)
    {
        if (tenantId == 0)
        {
            var user = await _context.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId && u.IsActive && !u.RequiresPasswordSetup, cancellationToken);
            return user is null ? null : ToAuthUser(user, null);
        }
        var membership = await ActiveMembership(userId, tenantId).FirstOrDefaultAsync(cancellationToken);
        return membership == null ? null : ToAuthUser(membership.User, membership);
    }

    private IQueryable<TenantMembership> ActiveMembership(int userId, int tenantId) =>
        _context.TenantMemberships.Include(m => m.User).Where(m => m.UserId == userId && m.TenantId == tenantId
            && m.IsActive && m.User.IsActive && !m.User.RequiresPasswordSetup && m.Tenant.IsActive);

    public async Task<AuthDTO> SwitchTenantAsync(int userId, int tenantId, CancellationToken cancellationToken = default, Guid? sessionId = null, SessionClientInfo? client = null)
    {
        if (tenantId == 0)
        {
            var user = await _context.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId && u.IsActive && !u.RequiresPasswordSetup, cancellationToken)
                ?? throw new KeyNotFoundException("Account not found.");
            return await SessionAsync(user, null, cancellationToken, sessionId, client);
        }
        var membership = await ActiveMembership(userId, tenantId).FirstOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException("Tenant not found.");
        return await SessionAsync(membership.User, membership, cancellationToken, sessionId, client);
    }

    private async Task<AuthDTO> SessionAsync(User user, TenantMembership? membership,
        CancellationToken cancellationToken, Guid? sessionId = null, SessionClientInfo? client = null)
    {
        var now = DateTime.UtcNow;
        AuthSession session;
        string? refreshToken = null;
        if (sessionId.HasValue)
        {
            session = await _context.AuthSessions.AsNoTracking().SingleOrDefaultAsync(s => s.Id == sessionId
                && s.UserId == user.Id && s.AuthVersion == user.AuthVersion && s.RevokedAt == null, cancellationToken)
                ?? throw new InvalidOperationException("Session is no longer active.");
            // SQLite restores DateTime without Kind; expiry remains UTC in both
            // JWT and DTO, including after a tenant switch.
            session.ExpiresAt = DateTime.SpecifyKind(session.ExpiresAt, DateTimeKind.Utc);
            if (session.ExpiresAt <= now) throw new InvalidOperationException("Session expired.");
            // The next refresh issues a token for the workspace the user switched to.
            var tenantId = membership?.TenantId;
            if (session.TenantId != tenantId)
            {
                await _context.AuthSessions.Where(s => s.Id == session.Id)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(s => s.TenantId, tenantId), cancellationToken);
                session.TenantId = tenantId;
            }
        }
        else
        {
            session = new AuthSession { UserId = user.Id, AuthVersion = user.AuthVersion, CreatedAt = now,
                TenantId = membership?.TenantId, UserAgent = Truncate(client?.UserAgent, 256),
                DeviceName = Truncate(client?.DeviceName, 100) };
            var clientType = client?.Client?.Trim().ToLowerInvariant();
            var lifetime = clientType switch
            {
                AuthSession.ClientWeb => _sessionOptions.Web,
                AuthSession.ClientMobile => _sessionOptions.Mobile,
                _ => null
            };
            if (lifetime is null)
            {
                // Clients that do not identify themselves cannot refresh: a plain 24h session.
                session.ClientType = AuthSession.ClientApi;
                session.ExpiresAt = now.AddHours(24);
            }
            else
            {
                session.ClientType = clientType!;
                session.ExpiresAt = now.AddDays(lifetime.AbsoluteDays);
                session.IdleExpiresAt = MinTime(now.AddDays(lifetime.IdleDays), session.ExpiresAt);
                session.LastUsedAt = now;
                refreshToken = NewRefreshToken();
                session.RefreshTokenHash = HashToken(refreshToken);
            }
            _context.AuthSessions.Add(session);
            await _context.SaveChangesAsync(cancellationToken);
        }
        return ToAuthDto(user, membership, session, refreshToken);
    }

    private AuthDTO ToAuthDto(User user, TenantMembership? membership, AuthSession session, string? refreshToken)
    {
        var accessToken = GenerateJwtToken(user, membership, session, out var accessExpiresAt);
        return new AuthDTO
        {
            AccessToken = accessToken, UserId = user.Id, Email = user.Email,
            ExpiresAt = Utc(session.ExpiresAt), AccessTokenExpiresAt = accessExpiresAt,
            RefreshToken = refreshToken, User = ToAuthUser(user, membership)
        };
    }

    // Same pattern as PasswordResetService: random 32 bytes as hex, stored as SHA-256 hex.
    private static string NewRefreshToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    private static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    private static DateTime MinTime(DateTime a, DateTime b) => a <= b ? a : b;
    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
    private static string? Truncate(string? value, int max)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed.Length <= max ? trimmed : trimmed[..max];
    }

    /// <summary>
    /// Exchanges a refresh token for a new access token and a rotated refresh token.
    /// Returns null when the session has ended (unknown, expired, revoked or reused token).
    /// </summary>
    public async Task<AuthDTO?> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken) || refreshToken.Length > 64) return null;
        var hash = HashToken(refreshToken.Trim());
        var now = DateTime.UtcNow;
        var session = await _context.AuthSessions.AsNoTracking().Include(s => s.User)
            .SingleOrDefaultAsync(s => s.RefreshTokenHash == hash || s.PreviousRefreshTokenHash == hash, cancellationToken);
        if (session is null) return null;

        var user = session.User;
        if (session.RevokedAt != null || session.AuthVersion != user.AuthVersion || !user.IsActive || user.RequiresPasswordSetup
            || Utc(session.ExpiresAt) <= now || session.IdleExpiresAt is null || Utc(session.IdleExpiresAt.Value) <= now)
            return null;

        if (session.RefreshTokenHash == hash)
        {
            var rotated = NewRefreshToken();
            var newHash = HashToken(rotated);
            var lifetime = session.ClientType == AuthSession.ClientWeb ? _sessionOptions.Web : _sessionOptions.Mobile;
            var idleExpiresAt = MinTime(now.AddDays(lifetime.IdleDays), Utc(session.ExpiresAt));
            // Conditional on the old hash: of two racing refreshes only one rotates.
            var updated = await _context.AuthSessions.Where(s => s.Id == session.Id && s.RefreshTokenHash == hash && s.RevokedAt == null)
                .ExecuteUpdateAsync(setters => setters.SetProperty(s => s.PreviousRefreshTokenHash, hash)
                    .SetProperty(s => s.RefreshTokenHash, newHash).SetProperty(s => s.RotatedAt, now)
                    .SetProperty(s => s.IdleExpiresAt, idleExpiresAt).SetProperty(s => s.LastUsedAt, now), cancellationToken);
            if (updated == 1)
                return ToAuthDto(user, await TenantMembershipForSession(session, cancellationToken), session, rotated);
            // Lost the race: the winner rotated, so this token is now the previous one.
            session = await _context.AuthSessions.AsNoTracking().Include(s => s.User)
                .SingleOrDefaultAsync(s => s.Id == session.Id, cancellationToken);
            if (session is null || session.RevokedAt != null || session.PreviousRefreshTokenHash != hash) return null;
        }

        var graceEnd = session.RotatedAt is { } rotatedAt
            ? Utc(rotatedAt).AddSeconds(_sessionOptions.RefreshGraceSeconds) : DateTime.MinValue;
        if (now > graceEnd)
        {
            // An old token reappeared after its successor was in use: assume it was stolen.
            await _context.AuthSessions.Where(s => s.Id == session.Id && s.RevokedAt == null)
                .ExecuteUpdateAsync(setters => setters.SetProperty(s => s.RevokedAt, now), cancellationToken);
            _logger.LogWarning("Refresh token reuse detected for user {UserId}, session {SessionId}; session revoked",
                session.UserId, session.Id);
            return null;
        }
        // Two tabs at once or a lost response: an access token only, the rotated refresh token stays as it is.
        return ToAuthDto(session.User, await TenantMembershipForSession(session, cancellationToken), session, null);
    }

    // The workspace the session last used, or null (personal scope) when that membership is gone.
    private async Task<TenantMembership?> TenantMembershipForSession(AuthSession session, CancellationToken cancellationToken) =>
        session.TenantId is { } tenantId && tenantId > 0
            ? await ActiveMembership(session.UserId, tenantId).FirstOrDefaultAsync(cancellationToken)
            : null;

    public async Task<List<SessionInfoDTO>> ListSessionsAsync(int userId, Guid currentSessionId, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var rows = await _context.AuthSessions.AsNoTracking()
            .Where(s => s.UserId == userId && s.RevokedAt == null && s.AuthVersion == s.User.AuthVersion
                && s.ExpiresAt > now && (s.IdleExpiresAt == null || s.IdleExpiresAt > now))
            .ToListAsync(cancellationToken);
        return rows.Select(s => new SessionInfoDTO { Id = s.Id, DeviceName = s.DeviceName, ClientType = s.ClientType,
                CreatedAt = Utc(s.CreatedAt), LastUsedAt = Utc(s.LastUsedAt ?? s.CreatedAt), Current = s.Id == currentSessionId })
            .OrderByDescending(s => s.Current).ThenByDescending(s => s.LastUsedAt).ToList();
    }

    /// <summary>Revokes one of the user's own sessions; false when it is not theirs or already ended.</summary>
    public async Task<bool> RevokeSessionAsync(int userId, Guid sessionId, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        return await _context.AuthSessions.Where(s => s.Id == sessionId && s.UserId == userId && s.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(s => s.RevokedAt, now), cancellationToken) == 1;
    }

    public async Task<int> RevokeOtherSessionsAsync(int userId, Guid currentSessionId, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        return await _context.AuthSessions.Where(s => s.UserId == userId && s.Id != currentSessionId && s.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(s => s.RevokedAt, now), cancellationToken);
    }

    public async Task LogoutAsync(int userId, Guid sessionId, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        await _context.AuthSessions.Where(s => s.Id == sessionId && s.UserId == userId && s.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(s => s.RevokedAt, now), cancellationToken);
    }

    /// <summary>
    /// Self-service password change (app#11). Verifies the current password
    /// against the stored hash and — only on a match — stores the hash of the
    /// new password. A wrong current password (or a missing user) returns
    /// false without revealing which check failed.
    /// </summary>
    public async Task<bool> ChangePasswordAsync(int userId, string currentPassword, string newPassword, CancellationToken cancellationToken = default)
    {
        var user = await _context.Users
            .AsNoTracking()
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

        var replacement = await HashPasswordAsync(newPassword);
        var now = DateTime.UtcNow;
        // Credential replacement and global revocation are one atomic write.
        // A concurrent change/deactivation must not be overwritten.
        var updated = await _context.Users.Where(u => u.Id == userId && u.IsActive
                && u.PasswordHash == user.PasswordHash && u.AuthVersion == user.AuthVersion)
            .ExecuteUpdateAsync(setters => setters.SetProperty(u => u.PasswordHash, replacement)
                .SetProperty(u => u.AuthVersion, u => u.AuthVersion + 1)
                .SetProperty(u => u.ModifiedAt, now), cancellationToken);
        if (updated != 1) return false;

        _logger.LogInformation("Password changed for user {UserId}", userId);
        return true;
    }

    private static AuthUserDTO ToAuthUser(User user, TenantMembership? membership) => new()
    {
        Id = user.Id,
        Name = user.Name,
        Email = user.Email,
        Role = (membership?.Role ?? UserRole.User).ToString(),
        TenantId = membership?.TenantId ?? 0,
        CreatedAt = user.CreatedAt,
        DefaultCurrency = user.DefaultCurrency,
        Language = user.Language,
        OnboardingCompleted = user.OnboardingCompletedAt != null
    };

    public Task<string> HashPasswordAsync(string password) => Task.FromResult(Passwords.Hash(password));

    // Verified against on failed logins for unknown accounts; never stored.
    private static readonly string DummyPasswordHash = Passwords.Hash(Guid.NewGuid().ToString("N"));

    /// <summary>
    /// (issue #80) Replaces a verified legacy single-round SHA-256 hash with
    /// PBKDF2 of the full submitted password. The conditional update leaves a
    /// concurrently changed hash untouched; the session stays valid because
    /// AuthVersion is unchanged (same password, stronger storage).
    /// </summary>
    private async Task UpgradeLegacyHashAsync(User user, string password, CancellationToken cancellationToken)
    {
        if (user.PasswordHash.StartsWith("pbkdf2-sha512:", StringComparison.Ordinal)) return;
        var upgraded = Passwords.Hash(password);
        var legacy = user.PasswordHash;
        var updated = await _context.Users.Where(u => u.Id == user.Id && u.PasswordHash == legacy)
            .ExecuteUpdateAsync(setters => setters.SetProperty(u => u.PasswordHash, upgraded)
                .SetProperty(u => u.ModifiedAt, DateTime.UtcNow), cancellationToken);
        if (updated == 1)
        {
            user.PasswordHash = upgraded;
            _logger.LogInformation("Upgraded legacy password hash for user {UserId}", user.Id);
        }
    }

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

            // Historic hashes were computed over the UTF-8 bytes truncated to
            // the UTF-16 character count, which drops trailing bytes of
            // non-ASCII passwords. Accept that layout for compatibility and the
            // full encoding; a successful login then upgrades to PBKDF2.
            var historic = LegacyHash(password, salt, truncateToCharCount: true);
            var full = LegacyHash(password, salt, truncateToCharCount: false);
            return CryptographicOperations.FixedTimeEquals(hash, historic)
                | CryptographicOperations.FixedTimeEquals(hash, full);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying stored password format");
            return false;
        }
    }

    // Preserve the original byte layout solely for verifying existing stored hashes.
    private static byte[] LegacyHash(string password, byte[] salt, bool truncateToCharCount)
    {
        var encoded = Encoding.UTF8.GetBytes(password);
        var length = truncateToCharCount ? Math.Min(password.Length, encoded.Length) : encoded.Length;
        var combined = new byte[salt.Length + length];
        Buffer.BlockCopy(salt, 0, combined, 0, salt.Length);
        Buffer.BlockCopy(encoded, 0, combined, salt.Length, length);
        return SHA256.HashData(combined);
    }

    private string GenerateJwtToken(User user, TenantMembership? membership, AuthSession session, out DateTime expiresAt)
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
            new Claim("sid", session.Id.ToString()),
            new Claim("auth_version", session.AuthVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new Claim(ClaimTypes.Role, (membership?.Role ?? UserRole.User).ToString()),
            new Claim("TenantId", (membership?.TenantId ?? 0).ToString()),
            new Claim("access_scope", membership is null ? "personal" : "tenant")
        };

        // Refreshable sessions get a short access token; others keep the session's lifetime.
        var sessionEnd = Utc(session.ExpiresAt);
        expiresAt = session.RefreshTokenHash is null
            ? sessionEnd
            : MinTime(DateTime.UtcNow.AddMinutes(_sessionOptions.AccessTokenMinutes), sessionEnd);

        var token = new JwtSecurityToken(
            issuer: _configuration["Jwt:Issuer"] ?? "Qontract",
            audience: _configuration["Jwt:Audience"] ?? "QontractClients",
            claims: claims,
            expires: expiresAt,
            signingCredentials: credentials
        );

        var jwtHandler = new JwtSecurityTokenHandler();
        var tokenString = jwtHandler.WriteToken(token);

        _logger.LogInformation("Generated JWT token for user: {Email}", user.Email);
        return tokenString;
    }
}
