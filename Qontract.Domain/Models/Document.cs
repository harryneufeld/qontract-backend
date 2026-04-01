using System.ComponentModel.DataAnnotations;

namespace Qontract.Domain.Models;

public class Document
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [Required]
    [MaxLength(255)]
    public string FileName { get; set; } = string.Empty;

    [Required]
    [MaxLength(200)]
    public string FileType { get; set; } = string.Empty;

    [Required]
    [MaxLength(500)]
    public string FilePath { get; set; } = string.Empty;

    [Required]
    public long FileSize { get; set; }

    [Required]
    [MaxLength(500)]
    public int UploadedBy { get; set; }

    [Required]
    public int ContractId { get; set; }

    [Required]
    [MaxLength(500)]
    public int CreatedBy { get; set; }

    [Required]
    [MaxLength(500)]
    public int ModifiedBy { get; set; }

    [Required]
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

    [Required]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Required]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [Required]
    public int Version { get; set; }

    // Navigation properties
    public Contract Contract { get; set; }
    public User? User { get; set; }
}
