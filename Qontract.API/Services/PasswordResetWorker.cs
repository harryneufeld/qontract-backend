using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Qontract.Application.Services;

namespace Qontract.API.Services;
public sealed class PasswordResetQueue
{
    private readonly Channel<string> _requests = Channel.CreateBounded<string>(
        new BoundedChannelOptions(128) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
    public bool TryEnqueue(string email) => _requests.Writer.TryWrite(email);
    public IAsyncEnumerable<string> ReadAllAsync(CancellationToken ct) => _requests.Reader.ReadAllAsync(ct);
}

public sealed class PasswordResetWorker(PasswordResetQueue queue, IServiceScopeFactory scopes,
    ILogger<PasswordResetWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var email in queue.ReadAllAsync(stoppingToken))
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<PasswordResetService>().DeliverAsync(email, timeout.Token);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                // SMTP/provider messages may contain recipient addresses or payloads.
                logger.LogWarning("Password-reset delivery failed ({FailureType}).", ex.GetType().Name);
            }
        }
    }
}
