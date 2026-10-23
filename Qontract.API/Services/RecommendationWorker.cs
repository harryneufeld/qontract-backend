using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Qontract.Application.Notifications;

namespace Qontract.API.Services;

/// <summary>
/// Runs the recommendation rules shortly after start-up and then on every
/// interval. Each workspace gets its own scope, so a failed save in one
/// workspace cannot leak into the next.
/// </summary>
public class RecommendationWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<RecommendationOptions> options,
    ILogger<RecommendationWorker> logger) : BackgroundService
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(45);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            logger.LogInformation("Recommendations are disabled (Recommendations:Enabled=false)");
            return;
        }

        var interval = TimeSpan.FromMinutes(Math.Max(1, options.Value.CheckIntervalMinutes));
        var delay = StartupDelay;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            delay = interval;

            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Keep the worker alive; the next tick runs the rules again.
                logger.LogError(ex, "Recommendation run failed; retrying on the next tick");
            }
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        List<(int? TenantId, string? Settings)> workspaces;
        using (var scope = scopeFactory.CreateScope())
            workspaces = await scope.ServiceProvider.GetRequiredService<RecommendationEngine>().WorkspacesAsync(ct);

        var changed = 0;
        foreach (var (tenantId, settings) in workspaces)
        {
            using var scope = scopeFactory.CreateScope();
            changed += await scope.ServiceProvider.GetRequiredService<RecommendationEngine>().RunWorkspaceAsync(tenantId, settings, now, ct);
        }

        using (var scope = scopeFactory.CreateScope())
            await scope.ServiceProvider.GetRequiredService<RecommendationEngine>().PurgeResolvedAsync(now, ct);
        logger.LogInformation("Recommendation run finished: {Changed} notification changes in {Workspaces} workspaces", changed, workspaces.Count);
    }
}
