using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Qontract.Domain.Models;

public class User : BaseEntity
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string Email { get; set; } = string.Empty;

    public string NormalizedEmail { get; set; } = string.Empty;

    [Required]
    [MaxLength(255)]
    public string PasswordHash { get; set; } = string.Empty;

    // Incremented atomically with credential changes; invalidates every session.
    [ConcurrencyCheck]
    public int AuthVersion { get; set; }

    [Required]
    [MaxLength(50)]
    public UserRole Role { get; set; }

    [Required]
    public bool IsActive { get; set; }

    [Required]
    public int TenantId { get; set; }

    // Navigation properties
    public Tenant? Tenant { get; set; }
}

public enum UserRole
{
    TenantAdmin,
    User,
    Contractor
}
