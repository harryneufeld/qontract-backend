namespace Qontract.Domain.Models;
// One row per accepted terms version: evidence of what the account agreed to and when.
public class TermsAcceptance
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int UserId { get; set; }
    public string Version { get; set; } = string.Empty;
    public DateTime AcceptedAt { get; set; }
    public User User { get; set; } = null!;
}
