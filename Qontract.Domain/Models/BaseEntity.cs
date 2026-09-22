using System.ComponentModel.DataAnnotations;

namespace Qontract.Domain.Models;

/// <summary>
/// Base class for all stored entities. Carries the audit trail: who
/// created and last modified the record, and when (issue #16).
///
/// Timestamps are maintained automatically by
/// <c>ApplicationDbContext</c>: <see cref="CreatedAt"/> is stamped when the
/// entity is first saved and <see cref="ModifiedAt"/> is refreshed on
/// every save.
/// </summary>
public abstract class BaseEntity
{
    [Required]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Required]
    public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;

    [MaxLength(500)]
    public string? CreatedBy { get; set; }

    [MaxLength(500)]
    public string? ModifiedBy { get; set; }
}
