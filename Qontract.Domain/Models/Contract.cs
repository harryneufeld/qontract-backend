using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Qontract.Domain.Models;

public class Contract : BaseEntity
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string? Description { get; set; }

    public string? Iban { get; set; }
    public string? Notes { get; set; }
    public ContractType? Type { get; set; }

    [Required]
    [MaxLength(50)]
    public ContractStatus Status { get; set; }

    [MaxLength(50)]
    public string? StartDate { get; set; }

    [MaxLength(50)]
    public string? EndDate { get; set; }

    [MaxLength(50)]
    public string? Currency { get; set; }

    [MaxLength(500)]
    public string? Value { get; set; }
    public BillingPeriod? BillingPeriod { get; set; }
    public ICollection<ContractPrice> PriceHistory { get; set; } = new List<ContractPrice>();

    [MaxLength(100)]
    public string? ContractNumber { get; set; }

    [MaxLength(50)]
    public string? MinimumContractDate { get; set; }

    // Notice period (Kündigungsfrist) and automatic renewal drive the
    // cancellation-deadline recommendation.
    public int? NoticePeriodValue { get; set; }
    public NoticePeriodUnit? NoticePeriodUnit { get; set; }
    public bool AutoRenews { get; set; }
    public int? RenewalMonths { get; set; }

    // Optional link to a contractor (a contract can reference a free-text
    // counterparty instead); must stay nullable.
    public int? ContractorId { get; set; }

    public int? TenantId { get; set; }

    [Required]
    public bool IsActive { get; set; }

    public int? OwnerUserId { get; set; }
    public int? ResponsibleUserId { get; set; }
    public User? ResponsibleUser { get; set; }
    public DateTime? DeletedAt { get; set; }
    [MaxLength(500)] public string? DeletedBy { get; set; }
    public DateTime? RetentionExpiresAt { get; set; }
    public User? Owner { get; set; }

    // Navigation properties
    public Tenant? Tenant { get; set; }
    public Contractor? Contractor { get; set; }
    public User? Creator { get; set; }
    public User? Modifier { get; set; }
    public ICollection<Document> Documents { get; set; } = new List<Document>();
}

public enum NoticePeriodUnit
{
    Days,
    Weeks,
    Months
}

public enum ContractStatus
{
    Draft,
    Active,
    Expired,
    Terminated
}

// Preserve legacy ContractType values, including explicit None.
public enum ContractType
{
    None = 0,
    Insurance = 1,
    Loan = 2,
    Subscription = 3,
    Other = 4
}
