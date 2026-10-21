using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Qontract.Application.Interfaces;
using Qontract.Core.Security;
using Qontract.Domain.Models;

namespace Qontract.Application.Services;

public class PasswordResetService(IApplicationDbContext db, IConfiguration config, IResetEmailSender sender)
{
    public static bool Enabled(IConfiguration config) => bool.TryParse(config["PasswordReset:Enabled"], out var enabled) && enabled;
    public static Uri PublicBase(IConfiguration config)
    {
        if (!Uri.TryCreate(config["PasswordReset:PublicAppUrl"], UriKind.Absolute, out var uri)
            || uri.Scheme != "https" || string.IsNullOrEmpty(uri.Host) || uri.UserInfo.Length != 0
            || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw new InvalidOperationException("PasswordReset:PublicAppUrl must be a trusted HTTPS app URL without credentials, query or fragment.");
        return uri;
    }

    // Worker-only: no account lookup or SMTP latency in the public request path.
    public async Task DeliverAsync(string email, CancellationToken cancellationToken = default, string language = "en")
    {
        if (!Enabled(config)) return;
        var normalized = email.Trim().ToUpperInvariant();
        var now = DateTime.UtcNow;
        PasswordReset reset;
        string token;
        string destination;
        await using (var transaction = await db.BeginSerializableTransactionAsync(cancellationToken))
        {
            var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.NormalizedEmail == normalized && u.IsActive, cancellationToken);
            if (user is null) return;
            // Database-backed limits cover multiple worker/API instances.
            var recent = await db.PasswordResets.Where(r => r.UserId == user.Id && r.CreatedAt > now.AddHours(-1))
                .Select(r => r.CreatedAt).ToListAsync(cancellationToken);
            if (recent.Count >= 3 || recent.Any(t => t > now.AddMinutes(-1))) return;
            await db.PasswordResets.Where(r => r.UserId == user.Id && r.ConsumedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.ConsumedAt, now), cancellationToken);
            token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            reset = new PasswordReset { UserId = user.Id, NormalizedEmail = normalized, AuthVersion = user.AuthVersion,
                TokenHash = Hash(token), CreatedAt = now, ExpiresAt = now.AddMinutes(30) };
            destination = user.Email;
            db.PasswordResets.Add(reset);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        language = Qontract.Application.Localization.SupportedLanguage.Normalize(language);
        var link = new UriBuilder(PublicBase(config)) { Fragment = "/reset-password?token=" + token + "&lang=" + language }.Uri;
        try { await sender.SendAsync(destination, link, cancellationToken, language); }
        catch
        {
            await db.PasswordResets.Where(r => r.Id == reset.Id && r.ConsumedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.ConsumedAt, DateTime.UtcNow), CancellationToken.None);
            throw;
        }
    }

    public async Task<bool> RedeemAsync(string token, string newPassword, CancellationToken cancellationToken = default)
    {
        if (!Enabled(config) || token is null || !Regex.IsMatch(token, @"\A[0-9A-F]{64}\z", RegexOptions.NonBacktracking)
            || string.IsNullOrEmpty(newPassword) || newPassword.Length < 6 || newPassword.Length > 255) return false;
        var replacement = Passwords.Hash(newPassword);
        var hash = Hash(token);
        var now = DateTime.UtcNow;
        await using var transaction = await db.BeginSerializableTransactionAsync(cancellationToken);
        var reset = await db.PasswordResets.AsNoTracking().SingleOrDefaultAsync(r => r.TokenHash == hash
            && r.ConsumedAt == null, cancellationToken);
        if (reset is null || reset.ExpiresAt <= now) return false;
        var consumed = await db.PasswordResets.Where(r => r.Id == reset.Id && r.ConsumedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.ConsumedAt, now), cancellationToken);
        if (consumed != 1) return false;
        var changed = await db.Users.Where(u => u.Id == reset.UserId && u.IsActive && u.AuthVersion == reset.AuthVersion
                && u.NormalizedEmail == reset.NormalizedEmail)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.PasswordHash, replacement)
                .SetProperty(u => u.AuthVersion, u => u.AuthVersion + 1)
                .SetProperty(u => u.RequiresPasswordSetup, false).SetProperty(u => u.ModifiedAt, now)
                // Following an emailed link proves the address as well.
                .SetProperty(u => u.EmailConfirmedAt, u => u.EmailConfirmedAt ?? now), cancellationToken);
        if (changed != 1) return false; // Transaction disposal rolls back token consumption.
        await db.PasswordResets.Where(r => r.UserId == reset.UserId && r.ConsumedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.ConsumedAt, now), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
