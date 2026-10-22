using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Configuration;
using MimeKit;
using Qontract.Application.Interfaces;

namespace Qontract.Infrastructure.Email;
public class SmtpResetEmailSender(IConfiguration config) : IResetEmailSender, IConfirmationEmailSender
{
    public static void ValidateConfiguration(IConfiguration config)
    {
        if (string.IsNullOrWhiteSpace(config["Smtp:Host"]) || !int.TryParse(config["Smtp:Port"], out var port)
            || port < 1 || port > 65535 || !MailboxAddress.TryParse(config["Smtp:From"], out _))
            throw new InvalidOperationException("SMTP host, port and valid sender are required for password reset and email confirmation.");
        Mode(config);
        if (string.IsNullOrEmpty(config["Smtp:Username"]) != string.IsNullOrEmpty(config["Smtp:Password"]))
            throw new InvalidOperationException("Configure both SMTP username/password or neither for a trusted relay.");
    }
    private static SecureSocketOptions Mode(IConfiguration config) => config["Smtp:TlsMode"] switch
    {
        null or "" or "StartTls" => SecureSocketOptions.StartTls,
        "SslOnConnect" => SecureSocketOptions.SslOnConnect,
        _ => throw new InvalidOperationException("Smtp:TlsMode must be StartTls or SslOnConnect.")
    };
    public MimeMessage CreateMessage(string email, Uri resetLink, string language = "en")
    {
        ValidateConfiguration(config);
        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(config["Smtp:From"]!));
        message.To.Add(MailboxAddress.Parse(email));
        var german = Qontract.Application.Localization.SupportedLanguage.Normalize(language) == "de";
        message.Subject = german ? "Dein qontract-Passwort festlegen" : "Set your qontract password";
        message.Body = new TextPart("plain") { Text =
            (german
                ? "Mit diesem Link kannst du ein neues qontract-Passwort festlegen. Er ist 30 Minuten gültig und kann einmal verwendet werden.\n\n"
                : "Use this link to set a new qontract password. It expires in 30 minutes and can be used once.\n\n")
            + resetLink.AbsoluteUri + (german
                ? "\n\nFalls du dies nicht angefordert hast, ignoriere diese Nachricht. Dein Passwort wurde nicht geändert."
                : "\n\nIf you did not request this, ignore this message. Your password has not changed.") };
        return message;
    }
    public MimeMessage CreateConfirmationMessage(string email, Uri confirmationLink, string code, string language = "en")
    {
        ValidateConfiguration(config);
        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(config["Smtp:From"]!));
        message.To.Add(MailboxAddress.Parse(email));
        var german = Qontract.Application.Localization.SupportedLanguage.Normalize(language) == "de";
        message.Subject = german ? "Bestätige deine E-Mail-Adresse für qontract" : "Confirm your email address for qontract";
        message.Body = new TextPart("plain") { Text =
            (german
                ? "Willkommen bei qontract! Gib diesen Code in der App ein, um deine E-Mail-Adresse zu bestätigen:\n\n    " + code
                    + "\n\nDer Code ist 30 Minuten gültig. Oder tippe auf diesen Link (24 Stunden gültig, einmal verwendbar):\n\n"
                : "Welcome to qontract! Enter this code in the app to confirm your email address:\n\n    " + code
                    + "\n\nThe code is valid for 30 minutes. Or tap this link (valid for 24 hours, usable once):\n\n")
            + confirmationLink.AbsoluteUri + (german
                ? "\n\nFalls du kein qontract-Konto erstellt hast, ignoriere diese Nachricht."
                : "\n\nIf you did not create a qontract account, ignore this message.") };
        return message;
    }
    public Task SendConfirmationAsync(string email, Uri confirmationLink, string code, CancellationToken cancellationToken, string language = "en") =>
        DeliverAsync(CreateConfirmationMessage(email, confirmationLink, code, language), cancellationToken);
    public Task SendAsync(string email, Uri resetLink, CancellationToken cancellationToken, string language = "en") =>
        DeliverAsync(CreateMessage(email, resetLink, language), cancellationToken);
    private async Task DeliverAsync(MimeMessage message, CancellationToken cancellationToken)
    {
        using var client = new SmtpClient { Timeout = 20_000 };
        // Mandatory TLS and default certificate verification; no protocol logging.
        await client.ConnectAsync(config["Smtp:Host"]!, int.Parse(config["Smtp:Port"]!), Mode(config), cancellationToken);
        if (!string.IsNullOrEmpty(config["Smtp:Username"]))
            await client.AuthenticateAsync(config["Smtp:Username"]!, config["Smtp:Password"]!, cancellationToken);
        await client.SendAsync(message, cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);
    }
}
