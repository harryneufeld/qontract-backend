using Qontract.Domain.Models;
using System.ComponentModel.DataAnnotations;

namespace Qontract.Application.Models;

public class ContractDTO
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public ContractStatus Status { get; set; }
    public string? StartDate { get; set; }
    public string? EndDate { get; set; }
    public string? Counterparty { get; set; }
    public string? Currency { get; set; }
    public string? Value { get; set; }
    public int? ContractorId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? ModifiedBy { get; set; }
    public DateTime? CreatedAtUTC { get; set; } = DateTime.UtcNow;
}

public class ContractCreateDTO
{
    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string? Description { get; set; }

    [Required]
    public ContractStatus Status { get; set; }

    [MaxLength(50)]
    public string? StartDate { get; set; }

    [MaxLength(50)]
    public string? EndDate { get; set; }

    [MaxLength(100)]
    public string? Counterparty { get; set; }

    [MaxLength(50)]
    public string? Currency { get; set; }

    [MaxLength(500)]
    public string? Value { get; set; }

    public int? ContractorId { get; set; }
}

public class ContractUpdateDTO
{
    [MaxLength(200)]
    public string? Title { get; set; }

    [MaxLength(2000)]
    public string? Description { get; set; }

    public ContractStatus? Status { get; set; }

    [MaxLength(50)]
    public string? StartDate { get; set; }

    [MaxLength(50)]
    public string? EndDate { get; set; }

    [MaxLength(100)]
    public string? Counterparty { get; set; }

    [MaxLength(50)]
    public string? Currency { get; set; }

    [MaxLength(500)]
    public string? Value { get; set; }

    [MaxLength(500)]
    public int? ContractorId { get; set; }
}
