namespace Qontract.Application.Services;

/// <summary>
/// Session lifetimes, bound from the "Auth:Sessions" configuration section.
/// The access token (JWT) is short-lived; the refresh token keeps the session
/// alive until its idle or absolute expiry, whichever comes first.
/// </summary>
public class AuthSessionOptions
{
    public const string SectionName = "Auth:Sessions";

    /// <summary>Lifetime of an access token issued for a refreshable session.</summary>
    public int AccessTokenMinutes { get; set; } = 15;

    /// <summary>
    /// How long (in seconds) after a rotation the previous refresh token is still
    /// accepted, for two tabs refreshing at once or a lost response. Within this
    /// window it yields an access token only; afterwards it counts as token theft.
    /// </summary>
    public int RefreshGraceSeconds { get; set; } = 30;

    public ClientSessionLifetime Web { get; set; } = new() { IdleDays = 30, AbsoluteDays = 90 };
    public ClientSessionLifetime Mobile { get; set; } = new() { IdleDays = 90, AbsoluteDays = 365 };

    /// <summary>Dead sessions are deleted after this many days.</summary>
    public int CleanupRetentionDays { get; set; } = 30;

    /// <summary>How often (in minutes) the cleanup worker runs.</summary>
    public int CleanupIntervalMinutes { get; set; } = 360;
}

public class ClientSessionLifetime
{
    public int IdleDays { get; set; }
    public int AbsoluteDays { get; set; }
}
