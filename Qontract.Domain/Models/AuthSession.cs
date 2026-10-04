namespace Qontract.Domain.Models;

public class AuthSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int UserId { get; set; }
    public int AuthVersion { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public User User { get; set; } = null!;
}
