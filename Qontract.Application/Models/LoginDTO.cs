using System.ComponentModel.DataAnnotations;

namespace Qontract.Application.Models;

public class LoginDTO
{
    [MaxLength(32)]
    public string? TwoFactorCode { get; set; }
    public bool UseRecoveryCode { get; set; }

    // "web" or "mobile"; see AuthSession.ClientType.
    [MaxLength(16)]
    public string? Client { get; set; }
    [MaxLength(100)]
    public string? DeviceName { get; set; }

    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    // No minimum: passwords set before the current policy must still sign in.
    [Required]
    [MaxLength(Qontract.Core.Security.PasswordPolicy.MaximumLength)]
    public string Password { get; set; } = string.Empty;
}