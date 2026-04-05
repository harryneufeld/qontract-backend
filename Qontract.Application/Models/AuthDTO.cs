using Qontract.Domain.Models;

namespace Qontract.Application.Models;

public class AuthDTO
{
    public string AccessToken { get; set; } = string.Empty;
    public int UserId { get; set; }
    public string Email { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
}