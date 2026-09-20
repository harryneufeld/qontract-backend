using System.ComponentModel.DataAnnotations;

namespace Qontract.Application.Models;

public class DocumentCreateDTO
{
    [Required]
    public string FileName { get; set; } = string.Empty;

    [Required]
    public string FileType { get; set; } = string.Empty;

    /// <summary>
    /// Base64-encoded file contents. The authoritative size is derived from
    /// the decoded bytes server-side — clients must not be trusted for it.
    /// </summary>
    [Required]
    public string FileData { get; set; } = string.Empty;

    [Required]
    public int ContractId { get; set; }
}
