using System.ComponentModel.DataAnnotations;

namespace Qontract.Application.Models;

public class ContractorDTO
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public ContractorType Type { get; set; }
    public string? ContactInfo { get; set; }
    public int TenantId { get; set; }
    public int? UserId { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? ModifiedBy { get; set; }
}

public class ContractorCreateDTO
{
    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [StringLength(50)]
    public ContractorType Type { get; set; }

    [MaxLength(500)]
    public string? ContactInfo { get; set; }

    public int TenantId { get; set; }

    [MaxLength(500)]
    public int? UserId { get; set; }
}

public class ContractorUpdateDTO
{
    [MaxLength(100)]
    public string? Name { get; set; }

    [StringLength(50)]
    public ContractorType? Type { get; set; }

    [MaxLength(500)]
    public string? ContactInfo { get; set; }

    public bool? IsActive { get; set; }
}
