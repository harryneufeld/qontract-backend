using Qontract.Domain.Models;

namespace Qontract.Application.Models;

public class AuthDTO
{
    public bool RequiresTwoFactor { get; set; }
    public string AccessToken { get; set; } = string.Empty;
    public int UserId { get; set; }
    public string Email { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
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
}