using System.Text.Json;

namespace Qontract.Application.Notifications;

public sealed class RuleSetting
{
    public bool Enabled { get; init; } = true;
    // Days before the due date at which a reminder is created, e.g. [30, 14, 3].
    public int[] LeadDays { get; init; } = [];
    public decimal MinPercent { get; init; }
    public int AfterMonths { get; init; }
}

/// <summary>
/// Workspace notification settings: the "notifications" object inside
/// <c>Tenant.Settings</c>. Anything left unset falls back to the defaults.
/// </summary>
public sealed class NotificationSettings
{
    public const string DefaultTimeZone = "Europe/Berlin";

    public string TimeZone { get; init; } = DefaultTimeZone;
    public bool UnassignedToAdmins { get; init; } = true;
    public IReadOnlyDictionary<string, RuleSetting> Rules { get; init; } = Defaults();

    public RuleSetting Rule(string kind) => Rules.TryGetValue(kind, out var rule) ? rule : new RuleSetting { Enabled = false };

    public static Dictionary<string, RuleSetting> Defaults() => new()
    {
        [NotificationKinds.NoticeDeadline] = new() { LeadDays = [30, 14, 3] },
        [NotificationKinds.EndDate] = new() { LeadDays = [30, 7] },
        [NotificationKinds.MinimumTerm] = new() { LeadDays = [30] },
        [NotificationKinds.PriceIncrease] = new() { MinPercent = 5 },
        [NotificationKinds.InsuranceReview] = new() { AfterMonths = 12 },
        [NotificationKinds.MissingData] = new() { Enabled = false },
        [NotificationKinds.AdminWithout2fa] = new()
    };

    public TimeZoneInfo ResolveTimeZone(string fallback)
    {
        foreach (var id in new[] { TimeZone, fallback, "UTC" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException) { }
        }
        return TimeZoneInfo.Utc;
    }

    /// <summary>Reads the settings; invalid values were rejected on save, so unknown parts are ignored here.</summary>
    public static NotificationSettings FromSettings(string? settings)
    {
        if (string.IsNullOrWhiteSpace(settings)) return new();
        try
        {
            using var json = JsonDocument.Parse(settings);
            if (json.RootElement.ValueKind != JsonValueKind.Object
                || !json.RootElement.TryGetProperty("notifications", out var node)
                || node.ValueKind != JsonValueKind.Object) return new();
            return Read(node, validate: false);
        }
        catch (JsonException) { return new(); }
    }

    /// <summary>Throws InvalidOperationException when the notifications part of the settings JSON is invalid.</summary>
    public static void Validate(string? settings)
    {
        if (string.IsNullOrWhiteSpace(settings)) return;
        try
        {
            using var json = JsonDocument.Parse(settings);
            if (json.RootElement.ValueKind == JsonValueKind.Object
                && json.RootElement.TryGetProperty("notifications", out var node))
            {
                if (node.ValueKind != JsonValueKind.Object) throw Invalid("Notification settings must be an object.");
                Read(node, validate: true);
            }
        }
        catch (JsonException) { throw new InvalidOperationException("Tenant settings must be valid JSON."); }
    }

    private static NotificationSettings Read(JsonElement node, bool validate)
    {
        var timeZone = DefaultTimeZone;
        var toAdmins = true;
        var rules = Defaults();
        foreach (var property in node.EnumerateObject())
        {
            switch (property.Name)
            {
                case "timeZone":
                    if (property.Value.ValueKind != JsonValueKind.String || !KnownTimeZone(property.Value.GetString()))
                    {
                        if (validate) throw Invalid("Time zone is not recognised.");
                        break;
                    }
                    timeZone = property.Value.GetString()!;
                    break;
                case "unassignedGoTo":
                    var target = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : null;
                    if (target is not ("admins" or "nobody"))
                    {
                        if (validate) throw Invalid("unassignedGoTo must be \"admins\" or \"nobody\".");
                        break;
                    }
                    toAdmins = target == "admins";
                    break;
                case "rules":
                    if (property.Value.ValueKind != JsonValueKind.Object)
                    {
                        if (validate) throw Invalid("Notification rules must be an object.");
                        break;
                    }
                    foreach (var rule in property.Value.EnumerateObject())
                    {
                        if (!rules.TryGetValue(rule.Name, out var baseline))
                        {
                            if (validate) throw Invalid($"Unknown notification rule \"{rule.Name}\".");
                            continue;
                        }
                        rules[rule.Name] = ReadRule(rule.Name, rule.Value, baseline, validate);
                    }
                    break;
                default:
                    if (validate) throw Invalid($"Unknown notification setting \"{property.Name}\".");
                    break;
            }
        }
        return new NotificationSettings { TimeZone = timeZone, UnassignedToAdmins = toAdmins, Rules = rules };
    }

    private static RuleSetting ReadRule(string name, JsonElement node, RuleSetting baseline, bool validate)
    {
        if (node.ValueKind != JsonValueKind.Object)
        {
            if (validate) throw Invalid($"Rule \"{name}\" must be an object.");
            return baseline;
        }
        var enabled = baseline.Enabled;
        var leadDays = baseline.LeadDays;
        var minPercent = baseline.MinPercent;
        var afterMonths = baseline.AfterMonths;
        foreach (var property in node.EnumerateObject())
        {
            try
            {
                switch (property.Name)
                {
                    case "enabled":
                        enabled = property.Value.ValueKind switch
                        {
                            JsonValueKind.True => true,
                            JsonValueKind.False => false,
                            _ => throw Invalid($"Rule \"{name}\": enabled must be true or false.")
                        };
                        break;
                    case "leadDays" when baseline.LeadDays.Length > 0:
                        if (property.Value.ValueKind != JsonValueKind.Array || property.Value.GetArrayLength() is < 1 or > 6)
                            throw Invalid($"Rule \"{name}\": leadDays needs 1 to 6 entries.");
                        leadDays = property.Value.EnumerateArray()
                            .Select(v => v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var d) && d is >= 1 and <= 365
                                ? d : throw Invalid($"Rule \"{name}\": leadDays must be whole numbers between 1 and 365."))
                            .Distinct().OrderByDescending(d => d).ToArray();
                        break;
                    case "minPercent" when name == NotificationKinds.PriceIncrease:
                        if (property.Value.ValueKind != JsonValueKind.Number || !property.Value.TryGetDecimal(out minPercent) || minPercent is < 0 or > 1000)
                            throw Invalid($"Rule \"{name}\": minPercent must be between 0 and 1000.");
                        break;
                    case "afterMonths" when name == NotificationKinds.InsuranceReview:
                        if (property.Value.ValueKind != JsonValueKind.Number || !property.Value.TryGetInt32(out afterMonths) || afterMonths is < 1 or > 120)
                            throw Invalid($"Rule \"{name}\": afterMonths must be between 1 and 120.");
                        break;
                    default:
                        throw Invalid($"Rule \"{name}\": unknown setting \"{property.Name}\".");
                }
            }
            catch (InvalidOperationException) when (!validate) { }
        }
        return new RuleSetting { Enabled = enabled, LeadDays = leadDays, MinPercent = minPercent, AfterMonths = afterMonths };
    }

    private static bool KnownTimeZone(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;
        try { TimeZoneInfo.FindSystemTimeZoneById(id); return true; }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException) { return false; }
    }

    private static InvalidOperationException Invalid(string message) => new(message);
}
