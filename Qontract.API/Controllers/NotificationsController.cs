using Microsoft.AspNetCore.Authorization;
using Qontract.Application.Models;
using Qontract.Application.Notifications;

namespace Qontract.API.Controllers;

[ApiController, Authorize(Policy = "Account"), ApiVersion("1.0")]
[Route("api/notifications"), Route("api/v{version:apiVersion}/notifications")]
public class NotificationsController(NotificationService service) : ControllerBase
{
    private int UserId => int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
        ?? throw new UnauthorizedAccessException());
    private int TenantId => int.Parse(User.FindFirst("TenantId")?.Value
        ?? throw new InvalidOperationException("Authenticated user has no TenantId claim."));

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) => Ok(await service.ListAsync(UserId, TenantId, ct));

    [HttpGet("summary")]
    public async Task<IActionResult> Summary(CancellationToken ct) => Ok(await service.SummaryAsync(UserId, TenantId, ct));

    [HttpPost("read-all")]
    public async Task<IActionResult> ReadAll(CancellationToken ct)
    {
        await service.MarkAllReadAsync(UserId, TenantId, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/read")]
    public async Task<IActionResult> Read(Guid id, CancellationToken ct)
    {
        await service.MarkReadAsync(UserId, id, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/dismiss")]
    public async Task<IActionResult> Dismiss(Guid id, CancellationToken ct)
    {
        await service.DismissAsync(UserId, id, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/snooze")]
    public async Task<IActionResult> Snooze(Guid id, NotificationSnoozeDTO dto, CancellationToken ct)
    {
        await service.SnoozeAsync(UserId, id, dto.Days, ct);
        return NoContent();
    }

    [HttpGet("preferences")]
    public async Task<IActionResult> GetPreferences(CancellationToken ct) => Ok(await service.GetPreferencesAsync(UserId, ct));

    [HttpPut("preferences")]
    public async Task<IActionResult> SetPreferences(NotificationPreferencesDTO dto, CancellationToken ct) =>
        Ok(await service.SetPreferencesAsync(UserId, dto, ct));
}
