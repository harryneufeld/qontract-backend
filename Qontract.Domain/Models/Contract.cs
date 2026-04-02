using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Qontract.Domain.Models;

public class Contract
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string? Description { get; set; }

    [Required]
    [MaxLength(50)]
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

    [Required]
    public int? ContractorId { get; set; }

    public int? TenantId { get; set; }

    [MaxLength(500)]
    public string? CreatedBy { get; set; }

    [MaxLength(500)]
    public string? ModifiedBy { get; set; }

    [Required]
    public bool IsActive { get; set; }

    [Required]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Required]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public Tenant? Tenant { get; set; }
    public Contractor? Contractor { get; set; }
    public User? Creator { get; set; }
    public User? Modifier { get; set; }
    public ICollection<Document> Documents { get; set; } = new List<Document>();
}

public enum ContractStatus
{
    Draft,
    Active,
    Expired,
    Terminated
}
