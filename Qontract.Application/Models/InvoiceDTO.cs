using System.ComponentModel.DataAnnotations;

namespace Qontract.Application.Models;

public class InvoiceInput
{
    [Required] public string InvoiceDate { get; set; } = "";
    public string? DueDate { get; set; }
    [Required] public string Amount { get; set; } = "";
    [MaxLength(3)] public string? Currency { get; set; }
    [MaxLength(100)] public string? InvoiceNumber { get; set; }
    [MaxLength(2000)] public string? Description { get; set; }
    public int? ContractId { get; set; }
    public int? DocumentId { get; set; }
}

public class InvoiceDTO : InvoiceInput
{
    public Guid Id { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ModifiedAt { get; set; }
}
