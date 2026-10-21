namespace Qontract.Application.Interfaces;
public interface IConfirmationEmailSender
{
    Task SendConfirmationAsync(string email, Uri confirmationLink, CancellationToken cancellationToken, string language = "en");
}
