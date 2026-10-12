using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Qontract.Application.Services;

namespace Qontract.API.Services;
public sealed record PasswordResetRequest(string Email, string Language);

public sealed class PasswordResetQueue
{
    private readonly Channel<PasswordResetRequest> _requests = Channel.CreateBounded<PasswordResetRequest>(
        new BoundedChannelOptions(128) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
    public bool TryEnqueue(string email, string language = "en") => _requests.Writer.TryWrite(new(email,
        Qontract.Application.Localization.SupportedLanguage.Normalize(language)));
    public IAsyncEnumerable<PasswordResetRequest> ReadAllAsync(CancellationToken ct) => _requests.Reader.ReadAllAsync(ct);
}

public sealed class PasswordResetWorker(PasswordResetQueue queue, IServiceScopeFactory scopes,
    ILogger<PasswordResetWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var request in queue.ReadAllAsync(stoppingToken))
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<PasswordResetService>().DeliverAsync(request.Email, timeout.Token, request.Language);
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
