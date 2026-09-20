namespace Qontract.Application.Models;

public class DocumentDTO
{
    public int Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string FileType { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public int ContractId { get; set; }
    public DateTime UploadedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public int Version { get; set; }
    public string? CreatedBy { get; set; }
    public string? ModifiedBy { get; set; }
}
