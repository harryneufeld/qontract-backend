using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Qontract.Application.Interfaces;
using Qontract.Application.Models;
using Qontract.Domain.Models;

namespace Qontract.Application.Notifications;

/// <summary>A user's inbox in the workspace they are signed in to (tenantId 0 is the personal workspace).</summary>
public class NotificationService(IApplicationDbContext db)
{
    private IQueryable<Notification> Inbox(int userId, int tenantId, HashSet<string> muted, DateTime now)
    {
        int? workspace = tenantId == 0 ? null : tenantId;
        var mutedKinds = muted.ToList();
        return db.Notifications.Where(n => n.UserId == userId && n.TenantId == workspace
            && n.ResolvedAt == null && n.DismissedAt == null
            && (n.SnoozedUntil == null || n.SnoozedUntil <= now)
            && !mutedKinds.Contains(n.Kind));
    }

    public async Task<List<NotificationDTO>> ListAsync(int userId, int tenantId, CancellationToken ct = default)
    {
        var muted = await MutedAsync(userId, ct);
        var rows = await Inbox(userId, tenantId, muted, DateTime.UtcNow).AsNoTracking()
            .OrderByDescending(n => n.Severity).ThenBy(n => n.DueOn == null).ThenBy(n => n.DueOn).ThenByDescending(n => n.CreatedAt)
            .Select(n => new
            {
                n.Id, n.Kind, n.Severity, n.ContractId, ContractTitle = n.Contract == null ? null : n.Contract.Title,
                n.DueOn, n.Params, n.CreatedAt, n.ReadAt
            })
            .Take(200).ToListAsync(ct);
        return rows.Select(n => new NotificationDTO
        {
            Id = n.Id, Kind = n.Kind, Severity = n.Severity, ContractId = n.ContractId, ContractTitle = n.ContractTitle,
            DueOn = n.DueOn, Params = ParseParams(n.Params), CreatedAt = n.CreatedAt, ReadAt = n.ReadAt
        }).ToList();
    }

    public async Task<NotificationSummaryDTO> SummaryAsync(int userId, int tenantId, CancellationToken ct = default)
    {
        var inbox = Inbox(userId, tenantId, await MutedAsync(userId, ct), DateTime.UtcNow).AsNoTracking();
        return new NotificationSummaryDTO
        {
            Total = await inbox.CountAsync(ct),
            Unread = await inbox.CountAsync(n => n.ReadAt == null, ct)
        };
    }

    public async Task MarkReadAsync(int userId, Guid id, CancellationToken ct = default)
    {
        var notification = await Owned(userId, id, ct);
        notification.ReadAt ??= DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task MarkAllReadAsync(int userId, int tenantId, CancellationToken ct = default)
    {
        int? workspace = tenantId == 0 ? null : tenantId;
        var now = DateTime.UtcNow;
        await db.Notifications.Where(n => n.UserId == userId && n.TenantId == workspace && n.ReadAt == null && n.ResolvedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, now).SetProperty(n => n.ModifiedAt, now), ct);
    }

    public async Task DismissAsync(int userId, Guid id, CancellationToken ct = default)
    {
        var notification = await Owned(userId, id, ct);
        notification.DismissedAt ??= DateTime.UtcNow;
        notification.ReadAt ??= notification.DismissedAt;
        await db.SaveChangesAsync(ct);
    }

    public async Task SnoozeAsync(int userId, Guid id, int days, CancellationToken ct = default)
    {
        if (days is < 1 or > 90) throw new InvalidOperationException("Snooze between 1 and 90 days.");
        var notification = await Owned(userId, id, ct);
        notification.SnoozedUntil = DateTime.UtcNow.AddDays(days);
        await db.SaveChangesAsync(ct);
    }

    public async Task<NotificationPreferencesDTO> GetPreferencesAsync(int userId, CancellationToken ct = default)
    {
        var user = await db.Users.AsNoTracking().Where(u => u.Id == userId && u.IsActive)
            .Select(u => new { u.NotificationDigest, u.MutedNotificationKinds }).SingleOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException("Account not found.");
        return new NotificationPreferencesDTO { Digest = user.NotificationDigest, MutedKinds = Split(user.MutedNotificationKinds).ToList() };
    }

    public async Task<NotificationPreferencesDTO> SetPreferencesAsync(int userId, NotificationPreferencesDTO dto, CancellationToken ct = default)
    {
        if (!Enum.IsDefined(dto.Digest)) throw new InvalidOperationException("Digest must be off, daily or weekly.");
        var muted = dto.MutedKinds.Select(k => k.Trim()).Where(k => k.Length > 0).Distinct().ToList();
        if (muted.Any(k => !NotificationKinds.All.Contains(k))) throw new InvalidOperationException("Unknown notification kind.");
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == userId && u.IsActive, ct)
            ?? throw new KeyNotFoundException("Account not found.");
        user.NotificationDigest = dto.Digest;
        user.MutedNotificationKinds = muted.Count == 0 ? null : string.Join(',', muted);
        await db.SaveChangesAsync(ct);
        return new NotificationPreferencesDTO { Digest = user.NotificationDigest, MutedKinds = muted };
    }

    private async Task<Notification> Owned(int userId, Guid id, CancellationToken ct) =>
        await db.Notifications.SingleOrDefaultAsync(n => n.Id == id && n.UserId == userId, ct)
            ?? throw new KeyNotFoundException("Notification not found.");

    private async Task<HashSet<string>> MutedAsync(int userId, CancellationToken ct) =>
        Split(await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.MutedNotificationKinds).SingleOrDefaultAsync(ct)).ToHashSet();

    private static IEnumerable<string> Split(string? value) =>
        (value ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static JsonElement ParseParams(string json)
    {
        try { return JsonDocument.Parse(json).RootElement.Clone(); }
        catch (JsonException) { return JsonDocument.Parse("{}").RootElement.Clone(); }
    }
}
