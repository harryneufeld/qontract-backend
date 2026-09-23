namespace Qontract.Application.Models;

// Validated by DocumentCreateValidator (issue #39), not DataAnnotations.
public class DocumentCreateDTO
{
    public string FileName { get; set; } = string.Empty;

    public string FileType { get; set; } = string.Empty;

    /// <summary>
    /// Base64-encoded file contents. The authoritative size is derived from
    /// the decoded bytes server-side — clients must not be trusted for it.
    /// </summary>
    public string FileData { get; set; } = string.Empty;

    public int ContractId { get; set; }
}
