using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Qontract.Domain.Models;

public class Address : BaseEntity
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [Required]
    [StringLength(50)]
    public EntityType EntityType { get; set; }

    [Required]
    [MaxLength(500)]
    public string? Street { get; set; }

    [MaxLength(100)]
    public string? City { get; set; }

    [MaxLength(20)]
    public string? Zip { get; set; }

    [MaxLength(100)]
    public string? Country { get; set; }

    /// <summary>
    /// Soft-delete flag (issue #32). "Deactivate" flips this to false instead
    /// of blanking the street/city fields; all reads filter on IsActive.
    /// </summary>
    [Column(TypeName = "INTEGER")]
    public bool IsActive { get; set; } = true;

    // Navigation properties
    public int? TenantId { get; set; }
    public int? ContractorId { get; set; }
    public int? ContractId { get; set; }

    public Tenant? Tenant { get; set; }
    public Contractor? Contractor { get; set; }
    public Contract? Contract { get; set; }
}

public enum EntityType
{
    Tenant,
    Contractor,
    Person,
    Contract
}
