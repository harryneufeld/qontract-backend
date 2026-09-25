namespace Qontract.Application.Models;

/// <summary>
/// Payload for the self-service password change (app#11). The endpoint
/// targets the authenticated user — there is no user id in the body.
/// </summary>
public class ChangePasswordDTO
{
    public string CurrentPassword { get; set; } = string.Empty;

    public string NewPassword { get; set; } = string.Empty;
}
