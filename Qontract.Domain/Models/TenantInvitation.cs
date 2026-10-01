namespace Qontract.Domain.Models;

public class TenantInvitation : BaseEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int TenantId { get; set; }
    public Tenant Tenant { get; set; } = null!;
    public string CodeHash { get; set; } = string.Empty;
    public string? Email { get; set; }
    public UserRole Role { get; set; } = UserRole.User;
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public DateTime? RedeemedAt { get; set; }
    public int? RedeemedByUserId { get; set; }
    public User? RedeemedByUser { get; set; }
}
