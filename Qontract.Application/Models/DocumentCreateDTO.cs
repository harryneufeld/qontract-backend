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

    /// <summary>Optional human-readable title (app issue #5). Blank is stored as null.</summary>
    public string? Title { get; set; }

    /// <summary>Optional user-chosen document date (app issue #5).</summary>
    public DateTime? DocumentDate { get; set; }
    public string? Description { get; set; }
    public string? Category { get; set; }
}
