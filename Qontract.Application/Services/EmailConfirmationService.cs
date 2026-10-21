using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Qontract.Application.Interfaces;
using Qontract.Domain.Models;

namespace Qontract.Application.Services;

public class EmailConfirmationService(IApplicationDbContext db, IConfiguration config, IConfirmationEmailSender sender)
{
    public static readonly TimeSpan LinkLifetime = TimeSpan.FromHours(24);

    // Off by default so existing deployments without SMTP keep working. When on,
    // new accounts cannot sign in until their address is confirmed.
    public static bool Required(IConfiguration config) =>
        bool.TryParse(config["Registration:RequireEmailConfirmation"], out var required) && required;

    // Worker-only: no account lookup or SMTP latency in the public request path.
    public async Task DeliverAsync(string email, CancellationToken cancellationToken = default, string language = "en")
    {
        if (!Required(config)) return;
        var normalized = email.Trim().ToUpperInvariant();
        var now = DateTime.UtcNow;
        EmailConfirmation confirmation;
        string token;
        string destination;
        await using (var transaction = await db.BeginSerializableTransactionAsync(cancellationToken))
        {
            var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.NormalizedEmail == normalized
                && u.IsActive && u.EmailConfirmedAt == null, cancellationToken);
            if (user is null) return;
            // Database-backed limits cover multiple worker/API instances.
            var recent = await db.EmailConfirmations.Where(c => c.UserId == user.Id && c.CreatedAt > now.AddHours(-1))
                .Select(c => c.CreatedAt).ToListAsync(cancellationToken);
            if (recent.Count >= 3 || recent.Any(t => t > now.AddMinutes(-1))) return;
            await db.EmailConfirmations.Where(c => c.UserId == user.Id && c.ConsumedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.ConsumedAt, now), cancellationToken);
            token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            confirmation = new EmailConfirmation { UserId = user.Id, NormalizedEmail = normalized,
                TokenHash = Hash(token), CreatedAt = now, ExpiresAt = now.Add(LinkLifetime) };
            destination = user.Email;
            db.EmailConfirmations.Add(confirmation);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        language = Qontract.Application.Localization.SupportedLanguage.Normalize(language);
        var link = new UriBuilder(PasswordResetService.PublicBase(config)) { Fragment = "/confirm-email?token=" + token + "&lang=" + language }.Uri;
        try { await sender.SendConfirmationAsync(destination, link, cancellationToken, language); }
        catch
        {
            await db.EmailConfirmations.Where(c => c.Id == confirmation.Id && c.ConsumedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.ConsumedAt, DateTime.UtcNow), CancellationToken.None);
            throw;
        }
    }

    public async Task<bool> ConfirmAsync(string token, CancellationToken cancellationToken = default)
    {
        if (token is null || !Regex.IsMatch(token, @"\A[0-9A-F]{64}\z", RegexOptions.NonBacktracking)) return false;
        var hash = Hash(token);
        var now = DateTime.UtcNow;
        await using var transaction = await db.BeginSerializableTransactionAsync(cancellationToken);
        var confirmation = await db.EmailConfirmations.AsNoTracking().SingleOrDefaultAsync(c => c.TokenHash == hash
            && c.ConsumedAt == null, cancellationToken);
        if (confirmation is null || confirmation.ExpiresAt <= now) return false;
        var consumed = await db.EmailConfirmations.Where(c => c.Id == confirmation.Id && c.ConsumedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.ConsumedAt, now), cancellationToken);
        if (consumed != 1) return false;
        // The address must still be the one the link was sent to.
        var changed = await db.Users.Where(u => u.Id == confirmation.UserId && u.IsActive
                && u.NormalizedEmail == confirmation.NormalizedEmail)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.EmailConfirmedAt, u => u.EmailConfirmedAt ?? now)
                .SetProperty(u => u.ModifiedAt, now), cancellationToken);
        if (changed != 1) return false; // Transaction disposal rolls back token consumption.
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
