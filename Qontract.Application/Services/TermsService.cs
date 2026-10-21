using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Qontract.Application.Interfaces;
using Qontract.Domain.Models;

namespace Qontract.Application.Services;

public record TermsStatusDTO(string? Version, string? Url, bool Accepted);

// Terms of use are operator-specific: a self-hosted instance without
// Terms:Version shows no terms and records no acceptance.
public class TermsService(IApplicationDbContext db, IConfiguration config)
{
    public static string? CurrentVersion(IConfiguration config) =>
        string.IsNullOrWhiteSpace(config["Terms:Version"]) ? null : config["Terms:Version"]!.Trim();

    public static string? Url(IConfiguration config) => CurrentVersion(config) is null ? null : config["Terms:Url"];

    public static void ValidateConfiguration(IConfiguration config)
    {
        var version = CurrentVersion(config);
        if (version is null) return;
        if (version.Length > 50) throw new InvalidOperationException("Terms:Version must be at most 50 characters.");
        if (!Uri.TryCreate(config["Terms:Url"], UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.UserInfo.Length != 0)
            throw new InvalidOperationException("Terms:Url must be an HTTPS URL when Terms:Version is set.");
    }

    // Registration passes the version the user saw; a stale client must reload the current terms.
    public void EnsureAccepted(string? acceptedVersion)
    {
        var current = CurrentVersion(config);
        if (current is not null && !string.Equals(acceptedVersion?.Trim(), current, StringComparison.Ordinal))
            throw new InvalidOperationException("Please accept the current terms of use.");
    }

    public void Record(User user, DateTime acceptedAt)
    {
        var current = CurrentVersion(config);
        if (current is not null)
            db.TermsAcceptances.Add(new TermsAcceptance { User = user, Version = current, AcceptedAt = acceptedAt });
    }

    public async Task<TermsStatusDTO> StatusAsync(int userId, CancellationToken cancellationToken = default)
    {
        var current = CurrentVersion(config);
        var accepted = current is null || await db.TermsAcceptances.AnyAsync(a => a.UserId == userId && a.Version == current, cancellationToken);
        return new TermsStatusDTO(current, Url(config), accepted);
    }

    public async Task AcceptAsync(int userId, string? version, CancellationToken cancellationToken = default)
    {
        EnsureAccepted(version);
        var current = CurrentVersion(config);
        if (current is null || await db.TermsAcceptances.AnyAsync(a => a.UserId == userId && a.Version == current, cancellationToken)) return;
        db.TermsAcceptances.Add(new TermsAcceptance { UserId = userId, Version = current, AcceptedAt = DateTime.UtcNow });
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException)
        {
            // A concurrent request may have recorded the same acceptance (unique index).
            if (!await db.TermsAcceptances.AsNoTracking().AnyAsync(a => a.UserId == userId && a.Version == current, CancellationToken.None)) throw;
        }
    }
}
