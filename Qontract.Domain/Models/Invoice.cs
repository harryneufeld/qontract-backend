namespace Qontract.Domain.Models;

public class Invoice : BaseEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int TenantId { get; set; }
    public Tenant Tenant { get; set; } = null!;
    public DateOnly InvoiceDate { get; set; }
    public DateOnly? DueDate { get; set; }
    public decimal Amount { get; set; }
    public string? Currency { get; set; }
    public string? InvoiceNumber { get; set; }
    public string? Description { get; set; }
    public int? ContractId { get; set; }
    public Contract? Contract { get; set; }
    public int? DocumentId { get; set; }
    public Document? Document { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
}
