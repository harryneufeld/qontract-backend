namespace Qontract.Domain.Models;
public class EmailConfirmation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int UserId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    // Hash of the 6-digit code that is mailed next to the link; null on rows created before codes existed.
    public string? CodeHash { get; set; }
    public DateTime? CodeExpiresAt { get; set; }
    public int CodeAttempts { get; set; }
    public string NormalizedEmail { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? ConsumedAt { get; set; }
    public User User { get; set; } = null!;
}
