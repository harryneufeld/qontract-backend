using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

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

    [MaxLength(500)]
    public string? CreatedBy { get; set; }

    [MaxLength(500)]
    public string? ModifiedBy { get; set; }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ContractorType
{
    Individual,
    Company
}
