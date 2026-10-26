using Qontract.Domain.Models;

namespace Qontract.Application.Models;

public class AuthDTO
{
    public bool RequiresTwoFactor { get; set; }
    public string AccessToken { get; set; } = string.Empty;
    public int UserId { get; set; }
    public string Email { get; set; } = string.Empty;
    // Absolute expiry of the session; the access token itself expires at AccessTokenExpiresAt.
    public DateTime ExpiresAt { get; set; }
    public DateTime AccessTokenExpiresAt { get; set; }
    // Mobile only: web clients get it as the HttpOnly cookie instead.
    public string? RefreshToken { get; set; }
    public AuthUserDTO? User { get; set; }
}

/// <summary>
/// Public user representation — never includes PasswordHash or other secrets.
/// </summary>
public class AuthUserDTO
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public int TenantId { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? DefaultCurrency { get; set; }
    public string? Language { get; set; }
    // False until first-run setup (name, currency, language) has been completed or skipped.
    public bool OnboardingCompleted { get; set; }
}

public class ProfileUpdateDTO
{
    [System.ComponentModel.DataAnnotations.StringLength(100)] public string? Name { get; set; }
    [System.ComponentModel.DataAnnotations.StringLength(3, MinimumLength = 3)] public string? DefaultCurrency { get; set; }
    [System.ComponentModel.DataAnnotations.StringLength(5)] public string? Language { get; set; }
    public bool? OnboardingCompleted { get; set; }
}
/// <summary>What the client says about itself when a session starts.</summary>
public record SessionClientInfo(string? Client, string? DeviceName, string? UserAgent);

public class RefreshRequestDTO
{
    [System.ComponentModel.DataAnnotations.StringLength(64)]
    public string? RefreshToken { get; set; }
}

public class SessionInfoDTO
{
    public Guid Id { get; set; }
    public string? DeviceName { get; set; }
    public string ClientType { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime LastUsedAt { get; set; }
    public bool Current { get; set; }
}
