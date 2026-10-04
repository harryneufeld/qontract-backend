using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Qontract.Domain.Models;

public class Contractor : BaseEntity
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

    [MaxLength(200)]
    public string? Email { get; set; }

    [MaxLength(50)]
    public string? Phone { get; set; }
    public string? Website { get; set; }
    public string? Description { get; set; }
    public string? Notes { get; set; }

    public int? TenantId { get; set; }

    [MaxLength(500)]
    public int? UserId { get; set; }

    [Required]
    public bool IsActive { get; set; }

    public int? OwnerUserId { get; set; }
    public User? Owner { get; set; }

    // Navigation properties
    public Tenant? Tenant { get; set; }
    public User? User { get; set; }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ContractorType
{
    Individual,
    Company
}
