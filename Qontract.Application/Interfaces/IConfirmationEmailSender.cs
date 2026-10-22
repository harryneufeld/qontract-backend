namespace Qontract.Application.Interfaces;
public interface IConfirmationEmailSender
{
    Task SendConfirmationAsync(string email, Uri confirmationLink, string code, CancellationToken cancellationToken, string language = "en");
}
