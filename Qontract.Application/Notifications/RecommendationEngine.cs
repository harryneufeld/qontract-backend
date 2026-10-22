using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Qontract.Application.Interfaces;
using Qontract.Domain.Models;

namespace Qontract.Application.Notifications;

/// <summary>
/// Server-wide switches for the recommendation worker. Bound from the
/// "Recommendations" configuration section.
/// </summary>
public class RecommendationOptions
{
    public const string SectionName = "Recommendations";

    public bool Enabled { get; set; } = true;

    /// <summary>How often the rules run. Reruns are harmless: every notification is deduplicated.</summary>
    public int CheckIntervalMinutes { get; set; } = 360;

    /// <summary>Time zone for workspaces (and personal contracts) that name none.</summary>
    public string DefaultTimeZone { get; set; } = NotificationSettings.DefaultTimeZone;
}

public sealed record RecommendationCandidate(
    int UserId, int? ContractId, string Kind, NotificationSeverity Severity,
    DateOnly? DueOn, string DedupKey, Dictionary<string, object?> Params);

/// <summary>
/// Evaluates the notification rules for every workspace and keeps the
/// Notifications table in step: new findings are added once per threshold,
/// findings whose condition went away are marked resolved.
/// </summary>
public class RecommendationEngine(IApplicationDbContext db, IOptions<RecommendationOptions> options, ILogger<RecommendationEngine> logger)
{
    private sealed record ContractFacts(
        int Id, ContractType? Type, ContractStatus Status, string? EndDate, string? MinimumContractDate,
        int? NoticePeriodValue, NoticePeriodUnit? NoticePeriodUnit, bool AutoRenews, int? RenewalMonths,
        int? ResponsibleUserId, int? OwnerUserId, int? ContractorId, DateTime CreatedAt, DateTime ModifiedAt,
        List<PriceFacts> Prices);

    private sealed record PriceFacts(string? Value, BillingPeriod? BillingPeriod, string EffectiveFrom, DateTime CreatedAt);

    private sealed record Member(int UserId, UserRole Role, bool TwoFactorEnabled);

    /// <summary>Every active tenant plus the personal workspaces (null), with the settings JSON of each.</summary>
    public async Task<List<(int? TenantId, string? Settings)>> WorkspacesAsync(CancellationToken ct = default)
    {
        var tenants = await db.Tenants.AsNoTracking().Where(t => t.IsActive)
            .Select(t => new { t.Id, t.Settings }).ToListAsync(ct);
        var workspaces = tenants.Select(t => ((int?)t.Id, t.Settings)).ToList();
        workspaces.Add((null, null));
        return workspaces;
    }

    /// <summary>Runs every workspace with this one context; the worker uses a scope per workspace instead.</summary>
    public async Task<int> RunAsync(DateTime utcNow, CancellationToken ct = default)
    {
        var changed = 0;
        foreach (var (tenantId, settings) in await WorkspacesAsync(ct))
            changed += await RunWorkspaceAsync(tenantId, settings, utcNow, ct);
        await PurgeResolvedAsync(utcNow, ct);
        return changed;
    }

    public Task PurgeResolvedAsync(DateTime utcNow, CancellationToken ct = default) =>
        db.Notifications.Where(n => n.ResolvedAt != null && n.ResolvedAt < utcNow.AddDays(-90)).ExecuteDeleteAsync(ct);

    public async Task<int> RunWorkspaceAsync(int? tenantId, string? settingsJson, DateTime utcNow, CancellationToken ct = default)
    {
        if (!options.Value.Enabled) return 0;
        var settings = NotificationSettings.FromSettings(settingsJson);
        try
        {
            var zone = settings.ResolveTimeZone(options.Value.DefaultTimeZone);
            var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utcNow, zone));

            var contracts = await db.Contracts.AsNoTracking()
                .Where(c => c.TenantId == tenantId && c.DeletedAt == null && c.IsActive)
                .Select(c => new ContractFacts(c.Id, c.Type, c.Status, c.EndDate, c.MinimumContractDate,
                    c.NoticePeriodValue, c.NoticePeriodUnit, c.AutoRenews, c.RenewalMonths,
                    c.ResponsibleUserId, c.OwnerUserId, c.ContractorId, c.CreatedAt, c.ModifiedAt,
                    c.PriceHistory.Select(p => new PriceFacts(p.Value, p.BillingPeriod, p.EffectiveFrom, p.CreatedAt)).ToList()))
                .ToListAsync(ct);

            var members = tenantId is null ? new List<Member>() : await db.TenantMemberships.AsNoTracking()
                .Where(m => m.TenantId == tenantId && m.IsActive && m.User.IsActive)
                .Select(m => new Member(m.UserId, m.Role, m.User.TwoFactorEnabled)).ToListAsync(ct);
            var admins = members.Where(m => m.Role == UserRole.TenantAdmin).Select(m => m.UserId).ToList();

            var documented = new HashSet<int>();
            if (settings.Rule(NotificationKinds.MissingData).Enabled && contracts.Count > 0)
            {
                var ids = contracts.Select(c => c.Id).ToList();
                documented = (await db.Documents.AsNoTracking()
                    .Where(d => !d.IsDeleted && d.SupersededById == null
                        && (d.ContractId != null && ids.Contains(d.ContractId.Value) || d.Links.Any(l => ids.Contains(l.ContractId))))
                    .Select(d => new { d.ContractId, Links = d.Links.Select(l => l.ContractId).ToList() }).ToListAsync(ct))
                    .SelectMany(d => d.Links.Concat(d.ContractId is int id ? [id] : []))
                    .ToHashSet();
            }

            var candidates = new List<RecommendationCandidate>();
            foreach (var contract in contracts)
            {
                var recipients = Recipients(contract, tenantId, members, admins, settings);
                if (recipients.Count == 0) continue;
                foreach (var finding in Evaluate(contract, settings, today, documented))
                    foreach (var userId in recipients)
                        candidates.Add(finding with { UserId = userId });
            }
            if (tenantId is not null && settings.Rule(NotificationKinds.AdminWithout2fa).Enabled)
                foreach (var admin in members.Where(m => m.Role == UserRole.TenantAdmin && !m.TwoFactorEnabled))
                    candidates.Add(new(admin.UserId, null, NotificationKinds.AdminWithout2fa, NotificationSeverity.Warning, null,
                        $"{NotificationKinds.AdminWithout2fa}:{tenantId}:{admin.UserId}", new()));

            return await ReconcileAsync(tenantId, candidates, utcNow, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // One broken workspace must not stop the others; the next run retries it.
            logger.LogError(ex, "Recommendations failed for workspace {TenantId}", tenantId);
            return 0;
        }
    }

    private static List<int> Recipients(ContractFacts contract, int? tenantId, List<Member> members, List<int> admins, NotificationSettings settings)
    {
        if (tenantId is null) return contract.OwnerUserId is int owner ? [owner] : [];
        if (contract.ResponsibleUserId is int responsible && members.Any(m => m.UserId == responsible)) return [responsible];
        return settings.UnassignedToAdmins ? admins : [];
    }

    private static IEnumerable<RecommendationCandidate> Evaluate(ContractFacts c, NotificationSettings settings, DateOnly today, HashSet<int> documented)
    {
        // Draft, expired and terminated contracts have no deadlines to act on.
        if (c.Status != ContractStatus.Active) yield break;

        var notice = settings.Rule(NotificationKinds.NoticeDeadline);
        if (notice.Enabled && NextDeadline(c, today) is var (deadline, termEnd)
            && Threshold((deadline.DayNumber - today.DayNumber), notice.LeadDays) is int noticeLead)
        {
            var days = deadline.DayNumber - today.DayNumber;
            yield return Finding(c, NotificationKinds.NoticeDeadline, Urgency(days), deadline, $"{deadline:yyyy-MM-dd}:{noticeLead}",
                new() { ["daysLeft"] = days, ["deadline"] = Iso(deadline), ["termEnd"] = Iso(termEnd), ["autoRenews"] = c.AutoRenews });
        }

        var end = settings.Rule(NotificationKinds.EndDate);
        if (end.Enabled && !c.AutoRenews && ParseDate(c.EndDate) is DateOnly endDate
            && Threshold(endDate.DayNumber - today.DayNumber, end.LeadDays) is int endLead)
        {
            var days = endDate.DayNumber - today.DayNumber;
            yield return Finding(c, NotificationKinds.EndDate, Urgency(days), endDate, $"{endDate:yyyy-MM-dd}:{endLead}",
                new() { ["daysLeft"] = days, ["endDate"] = Iso(endDate) });
        }

        var minimum = settings.Rule(NotificationKinds.MinimumTerm);
        if (minimum.Enabled && ParseDate(c.MinimumContractDate) is DateOnly minimumDate
            && Threshold(minimumDate.DayNumber - today.DayNumber, minimum.LeadDays) is int minimumLead)
        {
            var days = minimumDate.DayNumber - today.DayNumber;
            yield return Finding(c, NotificationKinds.MinimumTerm, Urgency(days), minimumDate, $"{minimumDate:yyyy-MM-dd}:{minimumLead}",
                new() { ["daysLeft"] = days, ["minimumDate"] = Iso(minimumDate) });
        }

        var price = settings.Rule(NotificationKinds.PriceIncrease);
        if (price.Enabled && PriceIncrease(c, today, price.MinPercent) is var (oldAmount, newAmount, percent, since))
            yield return Finding(c, NotificationKinds.PriceIncrease, NotificationSeverity.Warning, null, since,
                new() { ["from"] = oldAmount, ["to"] = newAmount, ["percent"] = percent, ["effectiveFrom"] = since });

        var review = settings.Rule(NotificationKinds.InsuranceReview);
        if (review.Enabled && c.Type == ContractType.Insurance)
        {
            var touched = DateOnly.FromDateTime(c.ModifiedAt);
            if (touched <= today.AddMonths(-review.AfterMonths))
                yield return Finding(c, NotificationKinds.InsuranceReview, NotificationSeverity.Info, null, $"{touched:yyyyMMdd}",
                    new() { ["monthsSinceUpdate"] = MonthsBetween(touched, today) });
        }

        var missing = settings.Rule(NotificationKinds.MissingData);
        if (missing.Enabled && DateOnly.FromDateTime(c.CreatedAt) <= today.AddDays(-7))
        {
            var noDocuments = !documented.Contains(c.Id);
            var noContractor = c.ContractorId is null;
            if (noDocuments || noContractor)
                yield return Finding(c, NotificationKinds.MissingData, NotificationSeverity.Info, null, $"{(noDocuments ? "d" : "")}{(noContractor ? "c" : "")}",
                    new() { ["noDocuments"] = noDocuments, ["noContractor"] = noContractor });
        }
    }

    private static RecommendationCandidate Finding(ContractFacts c, string kind, NotificationSeverity severity, DateOnly? dueOn, string keyPart, Dictionary<string, object?> parameters) =>
        new(0, c.Id, kind, severity, dueOn, $"{kind}:{c.Id}:{keyPart}", parameters);

    private static NotificationSeverity Urgency(int daysLeft) =>
        daysLeft <= 3 ? NotificationSeverity.Urgent : daysLeft <= 14 ? NotificationSeverity.Warning : NotificationSeverity.Info;

    /// <summary>The smallest lead time that has been reached, or null when the due date is past or not yet near enough.</summary>
    private static int? Threshold(int daysLeft, int[] leadDays)
    {
        if (daysLeft < 0) return null;
        int? reached = null;
        foreach (var lead in leadDays)
            if (lead >= daysLeft && (reached is null || lead < reached)) reached = lead;
        return reached;
    }

    /// <summary>
    /// The next day on which notice must be sent. Contracts that renew
    /// automatically roll their term forward until the deadline lies ahead.
    /// </summary>
    private static (DateOnly Deadline, DateOnly TermEnd)? NextDeadline(ContractFacts c, DateOnly today)
    {
        if (c.NoticePeriodValue is not > 0 || c.NoticePeriodUnit is null || ParseDate(c.EndDate) is not DateOnly termEnd) return null;
        var deadline = Subtract(termEnd, c.NoticePeriodValue.Value, c.NoticePeriodUnit.Value);
        if (c.AutoRenews && c.RenewalMonths is > 0)
            for (var i = 0; deadline < today && i < 240; i++)
            {
                termEnd = termEnd.AddMonths(c.RenewalMonths.Value);
                deadline = Subtract(termEnd, c.NoticePeriodValue.Value, c.NoticePeriodUnit.Value);
            }
        return deadline >= today ? (deadline, termEnd) : null;
    }

    private static DateOnly Subtract(DateOnly date, int value, NoticePeriodUnit unit) => unit switch
    {
        NoticePeriodUnit.Days => date.AddDays(-value),
        NoticePeriodUnit.Weeks => date.AddDays(-7 * value),
        _ => date.AddMonths(-value)
    };

    private static (decimal From, decimal To, decimal Percent, string Since)? PriceIncrease(ContractFacts c, DateOnly today, decimal minPercent)
    {
        var prices = c.Prices.Select(p => (Price: p, Amount: ParseAmount(p.Value)))
            .Where(p => p.Amount is > 0).OrderBy(p => p.Price.EffectiveFrom, StringComparer.Ordinal).ThenBy(p => p.Price.CreatedAt).ToList();
        if (prices.Count < 2) return null;
        var (latest, previous) = (prices[^1], prices[^2]);
        if (latest.Price.BillingPeriod != previous.Price.BillingPeriod || ParseDate(latest.Price.EffectiveFrom) is not DateOnly since) return null;
        if (since > today || since < today.AddDays(-60)) return null;
        var percent = Math.Round((latest.Amount!.Value - previous.Amount!.Value) / previous.Amount.Value * 100, 1);
        return percent > 0 && percent >= minPercent ? (previous.Amount.Value, latest.Amount.Value, percent, Iso(since)) : null;
    }

    private static decimal? ParseAmount(string? value) =>
        decimal.TryParse(value?.Trim().Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) ? amount : null;

    private static DateOnly? ParseDate(string? value) =>
        DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? DateOnly.FromDateTime(date) : null;

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static int MonthsBetween(DateOnly from, DateOnly to) => (to.Year - from.Year) * 12 + to.Month - from.Month;

    private async Task<int> ReconcileAsync(int? tenantId, List<RecommendationCandidate> candidates, DateTime utcNow, CancellationToken ct)
    {
        var existing = await db.Notifications.Where(n => n.TenantId == tenantId).ToListAsync(ct);
        var byKey = existing.ToDictionary(n => (n.UserId, n.DedupKey));
        var current = new HashSet<(int, string)>();
        var changed = 0;

        foreach (var candidate in candidates)
        {
            var key = (candidate.UserId, candidate.DedupKey);
            if (!current.Add(key)) continue;
            var json = JsonSerializer.Serialize(candidate.Params);
            if (byKey.TryGetValue(key, out var notification))
            {
                if (notification.ResolvedAt is not null) { notification.ResolvedAt = null; changed++; }
                notification.Severity = candidate.Severity;
                notification.DueOn = candidate.DueOn;
                notification.Params = json;
                continue;
            }
            db.Notifications.Add(new Notification
            {
                UserId = candidate.UserId, TenantId = tenantId, ContractId = candidate.ContractId,
                Kind = candidate.Kind, Severity = candidate.Severity, DueOn = candidate.DueOn,
                Params = json, DedupKey = candidate.DedupKey, CreatedBy = "system", ModifiedBy = "system"
            });
            changed++;
        }

        foreach (var notification in existing.Where(n => n.ResolvedAt is null && !current.Contains((n.UserId, n.DedupKey))))
        {
            notification.ResolvedAt = utcNow;
            changed++;
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            // Another instance inserted the same notification first; the next run reconciles.
            logger.LogWarning(ex, "Recommendation save for workspace {TenantId} conflicted and will be retried", tenantId);
            return 0;
        }
        return changed;
    }
}
