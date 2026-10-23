using System.ComponentModel.DataAnnotations;

namespace Qontract.Domain.Models;

public enum NotificationSeverity { Info, Warning, Urgent }

/// <summary>
/// A recommendation or reminder produced by the rule engine for one user.
/// Only the kind and its parameters are stored; the app renders the text in
/// the user's language. The unique (UserId, DedupKey) pair makes every rule
/// fire once per threshold, even when several API instances run the engine.
/// </summary>
public class Notification : BaseEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    // Null for the personal workspace.
    public int? TenantId { get; set; }
    public int? ContractId { get; set; }
    public Contract? Contract { get; set; }
    [Required, MaxLength(50)] public string Kind { get; set; } = string.Empty;
    public NotificationSeverity Severity { get; set; }
    public DateOnly? DueOn { get; set; }
    // JSON object with the numbers and dates the text needs; never contract content.
    [Required, MaxLength(2000)] public string Params { get; set; } = "{}";
    [Required, MaxLength(200)] public string DedupKey { get; set; } = string.Empty;
    public DateTime? ReadAt { get; set; }
    public DateTime? DismissedAt { get; set; }
    public DateTime? SnoozedUntil { get; set; }
    // Set when the condition behind the notification went away.
    public DateTime? ResolvedAt { get; set; }
}
