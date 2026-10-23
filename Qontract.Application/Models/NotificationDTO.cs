using System.Text.Json;
using Qontract.Domain.Models;

namespace Qontract.Application.Models;

public class NotificationDTO
{
    public Guid Id { get; set; }
    public string Kind { get; set; } = string.Empty;
    public NotificationSeverity Severity { get; set; }
    public int? ContractId { get; set; }
    public string? ContractTitle { get; set; }
    public DateOnly? DueOn { get; set; }
    // The numbers and dates the app needs to word the notification.
    public JsonElement Params { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ReadAt { get; set; }
}

public class NotificationSummaryDTO
{
    public int Unread { get; set; }
    public int Total { get; set; }
}

public class NotificationSnoozeDTO
{
    // How many days the notification stays hidden.
    public int Days { get; set; } = 7;
}

public class NotificationPreferencesDTO
{
    public NotificationDigest Digest { get; set; }
    public List<string> MutedKinds { get; set; } = new();
}
