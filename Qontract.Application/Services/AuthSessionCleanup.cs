using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Qontract.Application.Interfaces;

namespace Qontract.Application.Services;

/// <summary>
/// Deletes sessions that have been expired or revoked for longer than the
/// configured retention (30 days by default). Sessions that only just ended
/// stay for a while so reuse of their tokens is still recognised.
/// </summary>
public class AuthSessionCleanup(IApplicationDbContext context, IOptions<AuthSessionOptions> options,
    ILogger<AuthSessionCleanup> logger)
{
    public async Task<int> PurgeAsync(CancellationToken cancellationToken = default)
    {
        var cutoff = DateTime.UtcNow.AddDays(-Math.Max(1, options.Value.CleanupRetentionDays));
        var deleted = await context.AuthSessions
            .Where(s => s.ExpiresAt < cutoff || (s.IdleExpiresAt != null && s.IdleExpiresAt < cutoff)
                || (s.RevokedAt != null && s.RevokedAt < cutoff))
            .ExecuteDeleteAsync(cancellationToken);
        if (deleted > 0) logger.LogInformation("Deleted {Count} ended auth sessions", deleted);
        return deleted;
    }
}
