using Qontract.Domain.Models;

namespace Qontract.Application.Models;

public class ContractDTO
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public ContractStatus Status { get; set; }
    public string? StartDate { get; set; }
    public string? EndDate { get; set; }
    public string? Currency { get; set; }
    public string? Value { get; set; }
    public string? ContractNumber { get; set; }
    public string? MinimumContractDate { get; set; }
    public int? ContractorId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ModifiedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? ModifiedBy { get; set; }
    public DateTime? CreatedAtUTC { get; set; } = DateTime.UtcNow;
}

// DTO validation is done by the FluentValidation validators in
// Qontract.Application.FluentValidation (issue #39), not DataAnnotations.
public class ContractCreateDTO
{
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public ContractStatus Status { get; set; }
    public string? StartDate { get; set; }
    public string? EndDate { get; set; }
    public string? Currency { get; set; }
    public string? Value { get; set; }
    public string? ContractNumber { get; set; }
    public string? MinimumContractDate { get; set; }
    public int? ContractorId { get; set; }
}

public class ContractUpdateDTO
{
    public string? Title { get; set; }
    public string? Description { get; set; }
    public ContractStatus? Status { get; set; }
    public string? StartDate { get; set; }
    public string? EndDate { get; set; }
    public string? Currency { get; set; }
    public string? Value { get; set; }
    public string? ContractNumber { get; set; }
    public string? MinimumContractDate { get; set; }
    public int? ContractorId { get; set; }
}
