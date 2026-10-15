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

// Validated by UserUpdateValidator (issue #39), not DataAnnotations.
//
// (issue #80) Administrators manage memberships only. Account name, email and
// password belong to the account owner (self-service and recovery to their
// verified address), so they are deliberately absent here: an admin who could
// replace them could sign in as the member and read their personal workspace.
// New members join through invitations and choose their own password.
public class UserUpdateDTO
{
    public UserRole? Role { get; set; }

    public bool? IsActive { get; set; }
}
