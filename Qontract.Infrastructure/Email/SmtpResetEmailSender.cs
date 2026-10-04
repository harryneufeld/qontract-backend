using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Configuration;
using MimeKit;
using Qontract.Application.Interfaces;

namespace Qontract.Infrastructure.Email;
public class SmtpResetEmailSender(IConfiguration config) : IResetEmailSender
{
    public static void ValidateConfiguration(IConfiguration config)
    {
        if (string.IsNullOrWhiteSpace(config["Smtp:Host"]) || !int.TryParse(config["Smtp:Port"], out var port)
            || port < 1 || port > 65535 || !MailboxAddress.TryParse(config["Smtp:From"], out _))
            throw new InvalidOperationException("SMTP host, port and valid sender are required when password reset is enabled.");
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
    public async Task SendAsync(string email, Uri resetLink, CancellationToken cancellationToken)
    {
        ValidateConfiguration(config);
        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(config["Smtp:From"]));
        message.To.Add(MailboxAddress.Parse(email));
        message.Subject = "Set your qontract password";
        message.Body = new TextPart("plain") { Text =
            "Use this link to set a new qontract password. It expires in 30 minutes and can be used once.\n\n"
            + resetLink.AbsoluteUri + "\n\nIf you did not request this, ignore this message. Your password has not changed." };
        using var client = new SmtpClient { Timeout = 20_000 };
        // Mandatory TLS and default certificate verification; no protocol logging.
        await client.ConnectAsync(config["Smtp:Host"], int.Parse(config["Smtp:Port"]!), Mode(config), cancellationToken);
        if (!string.IsNullOrEmpty(config["Smtp:Username"]))
            await client.AuthenticateAsync(config["Smtp:Username"], config["Smtp:Password"], cancellationToken);
        await client.SendAsync(message, cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);
    }
}
