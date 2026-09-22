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

    [MaxLength(500)]
    public string? ContactInfo { get; set; }

    [Required]
    public int TenantId { get; set; }

    [MaxLength(500)]
    public int? UserId { get; set; }

    [Required]
    public bool IsActive { get; set; }

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
