namespace Qontract.Application.Models;

// Validated by DocumentReuploadValidator (issue #23), not DataAnnotations.
public class DocumentReuploadDTO
{
    /// <summary>
    /// Base64-encoded file contents of the new version. The authoritative
    /// size is derived from the decoded bytes server-side — clients must
    /// not be trusted for it.
    /// </summary>
    public string FileData { get; set; } = string.Empty;

    /// <summary>Optional new file name; the previous version's name is kept when blank.</summary>
    public string? FileName { get; set; }

    /// <summary>Optional new MIME type; the previous version's type is kept when blank.</summary>
    public string? FileType { get; set; }

    /// <summary>Optional title override; the previous version's title is kept when null.</summary>
    public string? Title { get; set; }

    /// <summary>Optional document date override; the previous version's date is kept when null.</summary>
    public DateTime? DocumentDate { get; set; }
}
