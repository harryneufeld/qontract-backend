namespace Qontract.Domain.Models;

// Additional contract associations. Document.ContractId remains the storage anchor.
public class DocumentLink : BaseEntity
{
    public int DocumentId { get; set; }
    public Document Document { get; set; } = null!;
    public int ContractId { get; set; }
    public Contract Contract { get; set; } = null!;
}
