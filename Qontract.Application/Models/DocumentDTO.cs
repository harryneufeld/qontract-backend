namespace Qontract.Application.Models;

public class DocumentDTO
{
    public int Id { get; set; }
    public string? Title { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string FileType { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public int ContractId { get; set; }
    public List<int> ContractIds { get; set; } = new();
    public DateTime UploadedAt { get; set; }
    public DateTime? DocumentDate { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ModifiedAt { get; set; }
    public int Version { get; set; }
    public string? CreatedBy { get; set; }
    public string? ModifiedBy { get; set; }

    /// <summary>Soft-delete timestamp (populated by the trash view only; null for active documents).</summary>
    public DateTime? DeletedAt { get; set; }

    /// <summary>When the retention purge permanently removes this document (trash view only).</summary>
    public DateTime? ExpiresAt { get; set; }
    public bool RetainedForInvoice { get; set; }
}
