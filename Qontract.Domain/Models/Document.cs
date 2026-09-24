using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Qontract.Domain.Models;

public class Document : BaseEntity
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
    public string UploadedBy { get; set; } = string.Empty;

    [Required]
    public int ContractId { get; set; }

    [Required]
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

    [Required]
    public int Version { get; set; }

    [MaxLength(255)]
    public string? Title { get; set; }

    /// <summary>
    /// User-chosen document date (app issue #5). Nullable: legacy
    /// documents carry no explicit date; the UI falls back to UploadedAt.
    /// </summary>
    public DateTime? DocumentDate { get; set; }

    /// <summary>
    /// Soft-delete flag (issue #14). Deleted documents stay in the database
    /// (and on disk) until the retention period has passed; list/get
    /// endpoints hide them. The retention purge removes row + file.
    /// </summary>
    public bool IsDeleted { get; set; }

    /// <summary>
    /// When the document was soft-deleted; the retention purge removes the
    /// row and file after the tenant's retention period.
    /// </summary>
    public DateTime? DeletedAt { get; set; }

    /// <summary>
    /// The version that superseded this document on re-upload (issue #23).
    /// Null for the current version of a document. Old versions stay in the
    /// database (and on disk) and remain individually downloadable.
    /// </summary>
    public int? SupersededById { get; set; }

    // Navigation properties
    public Contract Contract { get; set; }
    public User? User { get; set; }
    public ICollection<Contract> Contracts { get; set; } = new List<Contract>();
    public Document? SupersededBy { get; set; }
}
