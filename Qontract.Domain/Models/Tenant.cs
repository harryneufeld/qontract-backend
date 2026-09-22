using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Qontract.Domain.Models;

public class Tenant : BaseEntity
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [StringLength(50)]
    public TenantType Type { get; set; }

    [MaxLength(5000)]
    public string? Settings { get; set; }

    [Required]
    public bool IsActive { get; set; }
}

public enum TenantType
{
    Family,
    Company
}
