using System.ComponentModel.DataAnnotations;

namespace Qontract.Application.Models;

public class LoginDTO
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    // No minimum: passwords set before the current policy must still sign in.
    [Required]
    [MaxLength(Qontract.Core.Security.PasswordPolicy.MaximumLength)]
    public string Password { get; set; } = string.Empty;
}