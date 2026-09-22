using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Qontract.Application.Services;

namespace Qontract.API.Services;

/// <summary>
/// Deletion queue worker (issue #14): periodically runs the retention
/// purge that permanently removes soft-deleted documents once their
/// retention period has passed. Runs a first pass shortly after start-up
/// so purges missed while the app was down are caught up.
/// </summary>
public class DocumentRetentionWorker : BackgroundService
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(30);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly DocumentRetentionOptions _options;
    private readonly ILogger<DocumentRetentionWorker> _logger;

    public DocumentRetentionWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<DocumentRetentionOptions> options,
        ILogger<DocumentRetentionWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMinutes(Math.Max(1, _options.CheckIntervalMinutes));

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = stoppingToken.IsCancellationRequested
                ? TimeSpan.Zero
                : interval;
            // First pass shortly after start-up, then every interval.
            if (!_hasRun)
            {
                delay = StartupDelay;
            }

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var purger = scope.ServiceProvider.GetRequiredService<DocumentPurgeService>();
                await purger.PurgeExpiredAsync(stoppingToken);
                _hasRun = true;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Keep the worker alive; the next tick retries the purge.
                _logger.LogError(ex, "Document retention purge failed; retrying on the next tick");
            }
        }
    }

    private bool _hasRun;
}
