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
    public static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(30);
    public const int MaxCodeAttempts = 5;

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
        string code;
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
            code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
            confirmation = new EmailConfirmation { UserId = user.Id, NormalizedEmail = normalized,
                TokenHash = Hash(token), CodeHash = CodeHashFor(normalized, code), CodeExpiresAt = now.Add(CodeLifetime),
                CreatedAt = now, ExpiresAt = now.Add(LinkLifetime) };
            destination = user.Email;
            db.EmailConfirmations.Add(confirmation);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        language = Qontract.Application.Localization.SupportedLanguage.Normalize(language);
        var link = PasswordResetService.PathLink(config, "confirm-email", token, language);
        try { await sender.SendConfirmationAsync(destination, link, code, cancellationToken, language); }
        catch
        {
            await db.EmailConfirmations.Where(c => c.Id == confirmation.Id && c.ConsumedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.ConsumedAt, DateTime.UtcNow), CancellationToken.None);
            throw;
        }
    }

    public async Task<bool> ConfirmAsync(string token, CancellationToken cancellationToken = default) =>
        await ConfirmTokenAsync(token, cancellationToken) is not null;

    // Same as ConfirmAsync but returns the confirmed user so the caller can start a session.
    public async Task<int?> ConfirmTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        if (token is null || !Regex.IsMatch(token, @"\A[0-9A-F]{64}\z", RegexOptions.NonBacktracking)) return null;
        var hash = Hash(token);
        var now = DateTime.UtcNow;
        await using var transaction = await db.BeginSerializableTransactionAsync(cancellationToken);
        var confirmation = await db.EmailConfirmations.AsNoTracking().SingleOrDefaultAsync(c => c.TokenHash == hash
            && c.ConsumedAt == null, cancellationToken);
        if (confirmation is null || confirmation.ExpiresAt <= now) return null;
        var consumed = await db.EmailConfirmations.Where(c => c.Id == confirmation.Id && c.ConsumedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.ConsumedAt, now), cancellationToken);
        if (consumed != 1) return null;
        // The address must still be the one the link was sent to.
        var changed = await db.Users.Where(u => u.Id == confirmation.UserId && u.IsActive
                && u.NormalizedEmail == confirmation.NormalizedEmail)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.EmailConfirmedAt, u => u.EmailConfirmedAt ?? now)
                .SetProperty(u => u.ModifiedAt, now), cancellationToken);
        if (changed != 1) return null; // Transaction disposal rolls back token consumption.
        await transaction.CommitAsync(cancellationToken);
        return confirmation.UserId;
    }

    // Redeems the mailed 6-digit code. Returns the user id once, null for any wrong, expired or exhausted code.
    public async Task<int?> ConfirmCodeAsync(string email, string code, CancellationToken cancellationToken = default)
    {
        if (email is null || code is null || !Regex.IsMatch(code, @"\A[0-9]{6}\z", RegexOptions.NonBacktracking)) return null;
        var normalized = email.Trim().ToUpperInvariant();
        var now = DateTime.UtcNow;
        await using var transaction = await db.BeginSerializableTransactionAsync(cancellationToken);
        var confirmation = await db.EmailConfirmations.AsNoTracking()
            .Where(c => c.NormalizedEmail == normalized && c.ConsumedAt == null && c.CodeHash != null
                && c.CodeExpiresAt > now && c.ExpiresAt > now)
            .OrderByDescending(c => c.CreatedAt).FirstOrDefaultAsync(cancellationToken);
        if (confirmation is null) return null;
        if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(confirmation.CodeHash!),
                Encoding.ASCII.GetBytes(CodeHashFor(normalized, code))))
        {
            // The attempt counter must survive the failure: five wrong guesses burn the code.
            var attempts = confirmation.CodeAttempts + 1;
            var burn = attempts >= MaxCodeAttempts;
            await db.EmailConfirmations.Where(c => c.Id == confirmation.Id && c.ConsumedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.CodeAttempts, attempts)
                    .SetProperty(c => c.ConsumedAt, c => burn ? now : c.ConsumedAt), cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return null;
        }
        var consumed = await db.EmailConfirmations.Where(c => c.Id == confirmation.Id && c.ConsumedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.ConsumedAt, now), cancellationToken);
        if (consumed != 1) return null;
        var changed = await db.Users.Where(u => u.Id == confirmation.UserId && u.IsActive
                && u.NormalizedEmail == confirmation.NormalizedEmail)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.EmailConfirmedAt, u => u.EmailConfirmedAt ?? now)
                .SetProperty(u => u.ModifiedAt, now), cancellationToken);
        if (changed != 1) return null;
        await transaction.CommitAsync(cancellationToken);
        return confirmation.UserId;
    }

    private static string CodeHashFor(string normalizedEmail, string code) => Hash("code:" + normalizedEmail + ":" + code);

    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
