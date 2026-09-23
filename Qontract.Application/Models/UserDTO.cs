using Qontract.Domain.Models;

namespace Qontract.Application.Models;

public class UserDTO
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public UserRole Role { get; set; }
    public bool IsActive { get; set; }
    public int TenantId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ModifiedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? ModifiedBy { get; set; }
}

// Validated by UserCreateValidator / UserUpdateValidator (issue #39),
// not DataAnnotations.
public class UserCreateDTO
{
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// Plain password (NOT a hash) — the service hashes it before storing.
    /// </summary>
    public string Password { get; set; } = string.Empty;

    public UserRole Role { get; set; }

    public int TenantId { get; set; }
}

public class UserUpdateDTO
{
    public string? Name { get; set; }
    public string? Email { get; set; }

    /// <summary>
    /// Plain password (NOT a hash), only set when the password should be
    /// changed — the service hashes it before storing.
    /// </summary>
    public string? Password { get; set; }

    public UserRole? Role { get; set; }

    public bool? IsActive { get; set; }
}
