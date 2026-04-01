using System.ComponentModel.DataAnnotations;

namespace Qontract.Domain.Models;

public class Contractor
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [StringLength(50)]
    public ContractorType Type { get; set; }

    [MaxLength(500)]
    public string? ContactInfo { get; set; }

    [Required]
    public int TenantId { get; set; }

    [MaxLength(500)]
    public int? UserId { get; set; }

    [Required]
    public bool IsActive { get; set; }

    [Required]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Required]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public Tenant? Tenant { get; set; }
    public User? User { get; set; }
}

public enum ContractorType
{
    Individual,
    Company
}
