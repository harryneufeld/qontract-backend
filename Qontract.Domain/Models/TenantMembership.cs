namespace Qontract.Domain.Models;

/// <summary>A user's role and access in one tenant (backend #13).</summary>
public class TenantMembership : BaseEntity
{
    public int UserId { get; set; }
    public int TenantId { get; set; }
    public UserRole Role { get; set; }
    public bool IsActive { get; set; } = true;
    public User User { get; set; } = null!;
    public Tenant Tenant { get; set; } = null!;
}
