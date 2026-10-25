using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Qontract.Application.Services;

namespace Qontract.API.Services;

/// <summary>
/// Periodically deletes auth sessions that ended more than the retention period ago.
/// </summary>
public class AuthSessionCleanupWorker(IServiceScopeFactory scopeFactory, IOptions<AuthSessionOptions> options,
    ILogger<AuthSessionCleanupWorker> logger) : BackgroundService
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMinutes(Math.Max(1, options.Value.CleanupIntervalMinutes));
        var delay = StartupDelay;
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await Task.Delay(delay, stoppingToken); }
            catch (OperationCanceledException) { break; }
            delay = interval;

            try
            {
                using var scope = scopeFactory.CreateScope();
                await scope.ServiceProvider.GetRequiredService<AuthSessionCleanup>().PurgeAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Keep the worker alive; the next tick retries.
                logger.LogError(ex, "Auth session cleanup failed; retrying on the next tick");
            }
        }
    }
}
