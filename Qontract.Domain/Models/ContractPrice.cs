namespace Qontract.Domain.Models;

public enum BillingPeriod { OneTime, Daily, Weekly, Monthly, Quarterly, Yearly }

/// <summary>Dated price snapshot. EffectiveUntil is an exclusive date boundary.</summary>
public class ContractPrice : BaseEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int ContractId { get; set; }
    public Contract Contract { get; set; } = null!;
    public string? Value { get; set; }
    public string? Currency { get; set; }
    public BillingPeriod? BillingPeriod { get; set; }
    public string EffectiveFrom { get; set; } = string.Empty;
    public string? EffectiveUntil { get; set; }
    public string? Notes { get; set; }
}
