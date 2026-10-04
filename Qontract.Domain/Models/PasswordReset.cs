namespace Qontract.Domain.Models;
public class PasswordReset
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int UserId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public string NormalizedEmail { get; set; } = string.Empty;
    public int AuthVersion { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? ConsumedAt { get; set; }
    public User User { get; set; } = null!;
}
