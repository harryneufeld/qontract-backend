using Qontract.Domain.Models;

namespace Qontract.Application.Models;

public record ResponsibleMemberDTO(int Id, string Name);

public class ContractDTO
{
    public int? TenantId { get; set; }
    public int? OwnerUserId { get; set; }
    public int? ResponsibleUserId { get; set; }
    public string? ResponsibleUserName { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? DeletedAt { get; set; }
    public DateTime? RetentionExpiresAt { get; set; }
    public List<ContractPriceDTO> PriceHistory { get; set; } = new();
    public int Id { get; set; }
    public int DocumentCount { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Iban { get; set; }
    public string? Notes { get; set; }
    public ContractType? Type { get; set; }
    public ContractStatus Status { get; set; }
    public string? StartDate { get; set; }
    public string? EndDate { get; set; }
    public string? Currency { get; set; }
    public string? Value { get; set; }
    public BillingPeriod? BillingPeriod { get; set; }
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
    public bool ClearResponsibleUser { get; set; }
    public int? ResponsibleUserId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Iban { get; set; }
    public string? Notes { get; set; }
    public ContractType? Type { get; set; }
    public ContractStatus Status { get; set; }
    public string? StartDate { get; set; }
    public string? EndDate { get; set; }
    public string? Currency { get; set; }
    public string? Value { get; set; }
    public BillingPeriod? BillingPeriod { get; set; }
    public string? ContractNumber { get; set; }
    public string? MinimumContractDate { get; set; }
    public int? ContractorId { get; set; }
}

/// <summary>
/// Tenant-wide contract counters for the dashboard stat chips (app#33).
/// The dashboard list is paginated (issue #43), so the chips need totals
/// the client cannot derive from the loaded page. The date rules mirror
/// the app's client-side logic (Contract.isExpired / isExpiringSoon /
/// reachesMinimumContractDateIn).
/// </summary>
public class ContractStatsDTO
{
    public int Total { get; set; }
    public int Active { get; set; }
    public int Expiring { get; set; }
    public int Expired { get; set; }
    public int MinDateApproaching { get; set; }
}

public class ContractUpdateDTO
{
    public int? ResponsibleUserId { get; set; }
    public bool ClearResponsibleUser { get; set; }
    public bool? IsActive { get; set; }
    // Omitted/null Type preserves classification; ClearType explicitly resets it.
    public bool ClearType { get; set; }
    public string? PriceEffectiveFrom { get; set; }
    public string? PriceNotes { get; set; }
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? Iban { get; set; }
    public string? Notes { get; set; }
    public ContractType? Type { get; set; }
    public ContractStatus? Status { get; set; }
    public string? StartDate { get; set; }
    public string? EndDate { get; set; }
    public string? Currency { get; set; }
    public string? Value { get; set; }
    public BillingPeriod? BillingPeriod { get; set; }
    public string? ContractNumber { get; set; }
    public string? MinimumContractDate { get; set; }
    public int? ContractorId { get; set; }
}


public class ContractPriceDTO
{
    public Guid Id { get; set; }
    public string? Value { get; set; }
    public string? Currency { get; set; }
    public BillingPeriod? BillingPeriod { get; set; }
    public string EffectiveFrom { get; set; } = string.Empty;
    public string? EffectiveUntil { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
}
