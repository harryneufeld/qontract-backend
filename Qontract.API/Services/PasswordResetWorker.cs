using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Qontract.Application.Services;

namespace Qontract.API.Services;
public enum AccountEmailKind { PasswordReset, EmailConfirmation }
public sealed record PasswordResetRequest(string Email, string Language, AccountEmailKind Kind = AccountEmailKind.PasswordReset);

public sealed class PasswordResetQueue
{
    private readonly Channel<PasswordResetRequest> _requests = Channel.CreateBounded<PasswordResetRequest>(
        new BoundedChannelOptions(128) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
    public bool TryEnqueue(string email, string language = "en", AccountEmailKind kind = AccountEmailKind.PasswordReset) =>
        _requests.Writer.TryWrite(new(email, Qontract.Application.Localization.SupportedLanguage.Normalize(language), kind));
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
                if (request.Kind == AccountEmailKind.EmailConfirmation)
                    await scope.ServiceProvider.GetRequiredService<EmailConfirmationService>().DeliverAsync(request.Email, timeout.Token, request.Language);
                else
                    await scope.ServiceProvider.GetRequiredService<PasswordResetService>().DeliverAsync(request.Email, timeout.Token, request.Language);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                // SMTP/provider messages may contain recipient addresses or payloads.
                logger.LogWarning("{Kind} delivery failed ({FailureType}).", request.Kind, ex.GetType().Name);
            }
        }
    }
}
