using System.ComponentModel.DataAnnotations;

namespace Qontract.Application.Models;

public class DocumentCreateDTO
{
    [Required]
    public string FileName { get; set; } = string.Empty;

    [Required]
    public string FileType { get; set; } = string.Empty;

    public long FileSize { get; set; }

    [Required]
    public string FileData { get; set; } = string.Empty;

    [Required]
    public int ContractId { get; set; }
}