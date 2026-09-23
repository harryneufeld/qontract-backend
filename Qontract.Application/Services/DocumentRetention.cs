using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Qontract.Application.Interfaces;
using Qontract.Core.Interfaces;

namespace Qontract.Application.Services;

/// <summary>
/// Global settings for the document deletion queue (issue #14). Bound from
/// the "DocumentRetention" configuration section; defaults to a 90-day
/// (3 months) retention and an hourly check.
/// </summary>
public class DocumentRetentionOptions
{
    public const string SectionName = "DocumentRetention";

    /// <summary>Fallback retention in days when a tenant has no setting of its own.</summary>
    public int DefaultDays { get; set; } = 90;

    /// <summary>How often (in minutes) the retention purge checks the deletion queue.</summary>
    public int CheckIntervalMinutes { get; set; } = 60;
}

/// <summary>
/// Per-tenant settings live in <c>Tenant.Settings</c> as a JSON string, e.g.
/// <c>{"documentRetentionDays": 30}</c>.
/// </summary>
public static class TenantSettingsParser
{
    /// <summary>
    /// The tenant's document retention period in days, or null when the
    /// tenant has no (valid) setting — the global default then applies.
    /// </summary>
    public static int? GetDocumentRetentionDays(string? settingsJson)
    {
        if (string.IsNullOrWhiteSpace(settingsJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(settingsJson);
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("documentRetentionDays", out var element)
                && element.ValueKind == JsonValueKind.Number
                && element.TryGetInt32(out var days)
                && days > 0)
            {
                return days;
            }
        }
        catch (JsonException)
        {
            // Malformed tenant settings JSON: fall back to the global default.
        }
        catch (ArgumentException)
        {
            // Valid JSON but unexpected shape: fall back to the global default.
        }

        return null;
    }
}

/// <summary>
/// The document deletion queue (issue #14). Permanently removes
/// soft-deleted documents (database row plus stored file) once their
/// retention period has passed.
///
/// Retention resolution: per-tenant setting (<c>Tenant.Settings</c>) &gt;
/// global configuration (<c>DocumentRetention:DefaultDays</c>) &gt; 90 days.
/// </summary>
public class DocumentPurgeService
{
    private readonly IApplicationDbContext _context;
    private readonly IFileStorage _fileStorage;
    private readonly DocumentRetentionOptions _options;
    private readonly ILogger<DocumentPurgeService> _logger;

    public DocumentPurgeService(
        IApplicationDbContext context,
        IFileStorage fileStorage,
        IOptions<DocumentRetentionOptions> options,
        ILogger<DocumentPurgeService> logger)
    {
        _context = context;
        _fileStorage = fileStorage;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Permanently removes every soft-deleted document whose retention
    /// period has expired. Returns the number of purged documents.
    /// </summary>
    public async Task<int> PurgeExpiredAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var candidates = await _context.Documents
            .Where(d => d.IsDeleted && d.DeletedAt != null)
            .Include(d => d.Contract)
            .ThenInclude(c => c!.Tenant)
            .ToListAsync(cancellationToken);

        var purged = 0;
        foreach (var document in candidates)
        {
            var retentionDays =
                TenantSettingsParser.GetDocumentRetentionDays(document.Contract?.Tenant?.Settings)
                ?? _options.DefaultDays;
            var expiresAt = document.DeletedAt!.Value.AddDays(retentionDays);
            if (expiresAt > now)
            {
                continue;
            }

            // Best effort: the row must go even if the file is already gone.
            try
            {
                await _fileStorage.DeleteAsync(document.FilePath, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Purge: could not remove file {Path} for document {Id}", document.FilePath, document.Id);
            }

            _context.Documents.Remove(document);
            purged++;
            _logger.LogInformation(
                "Purged document {Id} ({File}) from contract {ContractId} after {RetentionDays} day retention (deleted {DeletedAt:u})",
                document.Id, document.FileName, document.ContractId, retentionDays, document.DeletedAt!.Value);
        }

        if (purged > 0)
        {
            await _context.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Deletion queue: permanently removed {Count} document(s)", purged);
        }
        else
        {
            _logger.LogInformation("Deletion queue: no soft-deleted document past its retention period");
        }

        return purged;
    }
}
