namespace Qontract.Application.Interfaces;
public interface IResetEmailSender
{
    Task SendAsync(string email, Uri resetLink, CancellationToken cancellationToken, string language = "en");
}
