using Qontract.Domain.Models;
using System.ComponentModel.DataAnnotations;

namespace Qontract.Application.Models;

public class TenantDTO
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public TenantType Type { get; set; }
    public string? Settings { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? ModifiedBy { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class TenantCreateDTO
{
    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [StringLength(50)]
    public TenantType Type { get; set; }

    [MaxLength(5000)]
    public string? Settings { get; set; }

    public bool IsActive { get; set; } = true;
}

public class TenantUpdateDTO
{
    [MaxLength(100)]
    public string? Name { get; set; }

    [StringLength(50)]
    public TenantType? Type { get; set; }

    [MaxLength(5000)]
    public string? Settings { get; set; }

    public bool? IsActive { get; set; }
}
