namespace Qontract.Domain.Models;

public class AuthSession
{
    public const string ClientWeb = "web";
    public const string ClientMobile = "mobile";
    // Clients that did not say what they are; no refresh token, 24h session.
    public const string ClientApi = "api";

    public Guid Id { get; set; } = Guid.NewGuid();
    public int UserId { get; set; }
    public int AuthVersion { get; set; }
    public DateTime CreatedAt { get; set; }
    // Absolute expiry; a refresh never moves it.
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }

    // SHA-256 hex of the current refresh token; null for sessions without refresh.
    public string? RefreshTokenHash { get; set; }
    public string? PreviousRefreshTokenHash { get; set; }
    public DateTime? RotatedAt { get; set; }
    public DateTime? IdleExpiresAt { get; set; }
    public DateTime? LastUsedAt { get; set; }

    public string ClientType { get; set; } = ClientApi;
    public string? DeviceName { get; set; }
    public string? UserAgent { get; set; }
    // Workspace the session is currently in; null is the personal scope.
    public int? TenantId { get; set; }

    public User User { get; set; } = null!;
}
