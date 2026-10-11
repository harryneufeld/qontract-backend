using Microsoft.EntityFrameworkCore;
using Qontract.Domain.Models;
using Qontract.Application.Models;
using Qontract.Core.Interfaces;
using Microsoft.Extensions.Options;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace Qontract.Application.Services;

public class DocumentService : IDocumentService
{
    private readonly IApplicationDbContext _context;
    private readonly IFileStorage _fileStorage;
    private readonly DocumentRetentionOptions _retentionOptions;
    private readonly IDocumentIdentity? _identity;

    public DocumentService(IApplicationDbContext context, IFileStorage fileStorage, IOptions<DocumentRetentionOptions> retentionOptions, IDocumentIdentity? identity = null)
    {
        _context = context;
        _fileStorage = fileStorage;
        _retentionOptions = retentionOptions.Value;
        _identity = identity;
    }

    public async Task<List<DocumentDTO>> GetDocumentsAsync(int contractId, int tenantId, CancellationToken cancellationToken = default)
    {
        if (!await _context.Contracts.VisibleIn(tenantId, _identity?.UserId).AnyAsync(c => c.Id == contractId && c.DeletedAt == null, cancellationToken))
            throw new KeyNotFoundException($"Contract with id {contractId} not found in tenant {tenantId}.");
        var documents = await VisibleDocuments(tenantId).AsNoTracking().Include(d => d.Links)
            .Where(d => d.Scope == (tenantId == 0 ? DocumentScope.Personal : DocumentScope.Tenant)
                && (d.ContractId == contractId || d.Links.Any(l => l.ContractId == contractId))
                && !d.IsDeleted && d.SupersededById == null)
            .OrderByDescending(d => d.ModifiedAt).ToListAsync(cancellationToken);
        return documents.Select(ToDto).ToList();
    }

    public async Task<DocumentDTO> GetDocumentByIdAsync(int id, int tenantId, CancellationToken cancellationToken = default)
    {
        var document = await VisibleDocuments(tenantId).AsNoTracking().Include(d => d.Links)
            .FirstOrDefaultAsync(d => d.Id == id && !d.IsDeleted, cancellationToken)
            ?? throw new KeyNotFoundException($"Document with id {id} not found in tenant {tenantId}.");
        return ToDto(document);
    }
    public async Task<DocumentDTO> CreateDocumentAsync(DocumentCreateDTO dto, int contractId, int tenantId, string? createdBy, CancellationToken cancellationToken = default)
        => await StoreUploadAsync(dto, contractId, tenantId, tenantId == 0 ? DocumentScope.Personal : DocumentScope.Tenant, createdBy, cancellationToken);

    public async Task<DocumentDTO> CreateLibraryDocumentAsync(DocumentCreateDTO dto, int tenantId, DocumentScope scope, string? actor, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(scope)) throw new InvalidOperationException("Unknown document scope.");
        if (_identity?.UserId is null) throw new UnauthorizedAccessException("An authenticated owner is required.");
        return await StoreUploadAsync(dto, null, tenantId, scope, actor, cancellationToken);
    }

    private async Task<DocumentDTO> StoreUploadAsync(DocumentCreateDTO dto, int? contractId, int tenantId, DocumentScope scope, string? createdBy, CancellationToken cancellationToken)
    {
        if (scope == DocumentScope.Tenant && tenantId <= 0 || scope == DocumentScope.Personal && _identity?.UserId is not > 0)
            throw new InvalidOperationException("A valid document workspace is required.");
        if (string.IsNullOrWhiteSpace(dto.FileName) || string.IsNullOrWhiteSpace(dto.FileType))
            throw new InvalidOperationException("File name and type are required.");
        ValidateMetadata(dto.FileName, dto.FileType, dto.Title, dto.Description, dto.Category);
        if (string.IsNullOrWhiteSpace(dto.FileData))
        {
            throw new InvalidOperationException("File data is required.");
        }

        byte[] fileBytes;
        try
        {
            fileBytes = Convert.FromBase64String(dto.FileData);
        }
        catch (FormatException)
        {
            throw new InvalidOperationException("File data is not valid base64.");
        }

        // The maximum file size is enforced by IFileStorage.SaveAsync
        // (issue #34): one definition, in the storage layer, not here.

        // Contract must exist and belong to the tenant.
        var contract = await _context.Contracts.VisibleIn(tenantId, _identity?.UserId)
            .FirstOrDefaultAsync(c => c.Id == contractId && c.DeletedAt == null, cancellationToken);
        if (contractId.HasValue && contract == null)
        {
            throw new KeyNotFoundException($"Contract with id {contractId} not found in tenant {tenantId}.");
        }

        // Reject path traversal / separator tricks in client-supplied file names.
        var safeFileName = Path.GetFileName(dto.FileName);
        if (string.IsNullOrWhiteSpace(safeFileName))
        {
            throw new InvalidOperationException("File name must not be empty.");
        }

        // A unique storage key prevents same-name uploads overwriting evidence.
        // All file I/O goes through IFileStorage (issue #34) so a cloud
        // backend (issue #25) can be swapped in without touching services.
        var filePath = _fileStorage.GenerateFilePath(contractId ?? 0, Guid.NewGuid().ToString("N") + ".bin", 1);
        await _fileStorage.SaveAsync(fileBytes, filePath, cancellationToken);

        var document = new Document
        {
            FileName = safeFileName,
            FileType = dto.FileType,
            FilePath = filePath,
            FileSize = fileBytes.Length, // trust the actual bytes, not the client's claim
            UploadedBy = createdBy ?? "system",
            ContractId = contractId,
            TenantId = scope == DocumentScope.Tenant ? tenantId : null, UserId = _identity?.UserId, Scope = scope, Description = NormalizeTitle(dto.Description), Category = NormalizeTitle(dto.Category),
            UploadedAt = DateTime.UtcNow,
            Title = NormalizeTitle(dto.Title),
            DocumentDate = dto.DocumentDate,
            CreatedAt = DateTime.UtcNow,
            ModifiedAt = DateTime.UtcNow,
            Version = 1,
            CreatedBy = createdBy ?? "system",
            ModifiedBy = createdBy ?? "system"
        };

        await _context.Documents.AddAsync(document, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        return ToDto(document);
    }

    public async Task<DocumentDTO> ReuploadDocumentAsync(int id, DocumentReuploadDTO dto, int tenantId, string? modifiedBy, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(dto.FileData))
        {
            throw new InvalidOperationException("File data is required.");
        }

        byte[] fileBytes;
        try
        {
            fileBytes = Convert.FromBase64String(dto.FileData);
        }
        catch (FormatException)
        {
            throw new InvalidOperationException("File data is not valid base64.");
        }

        // The maximum file size is enforced by IFileStorage.SaveAsync
        // (issue #34): one definition, in the storage layer, not here.

        await using var transaction = await _context.BeginSerializableTransactionAsync(cancellationToken);
        var current = await FindCurrentDocumentAsync(id, tenantId, cancellationToken);

        // Optional overrides; blank/absent keeps the previous version's value.
        var fileName = !string.IsNullOrWhiteSpace(dto.FileName)
            ? SanitizeFileName(dto.FileName)
            : current.FileName;
        var fileType = !string.IsNullOrWhiteSpace(dto.FileType)
            ? dto.FileType!
            : current.FileType;

        var newVersion = current.Version + 1;
        var filePath = _fileStorage.GenerateFilePath(current.ContractId ?? 0, Guid.NewGuid().ToString("N") + ".bin", newVersion);
        await _fileStorage.SaveAsync(fileBytes, filePath, cancellationToken);

        var document = new Document
        {
            FileName = fileName,
            FileType = fileType,
            FilePath = filePath,
            FileSize = fileBytes.Length, // trust the actual bytes, not the client's claim
            UploadedBy = modifiedBy ?? "system",
            ContractId = current.ContractId,
            TenantId = current.TenantId, UserId = current.UserId, Scope = current.Scope, Description = current.Description, Category = current.Category,
            UploadedAt = DateTime.UtcNow,
            Title = dto.Title is null ? current.Title : NormalizeTitle(dto.Title),
            DocumentDate = dto.DocumentDate ?? current.DocumentDate,
            CreatedAt = DateTime.UtcNow,
            ModifiedAt = DateTime.UtcNow,
            Version = newVersion,
            Links = current.Links.Select(l => new DocumentLink { ContractId = l.ContractId, CreatedBy = modifiedBy, ModifiedBy = modifiedBy }).ToList(),
            CreatedBy = modifiedBy ?? "system",
            ModifiedBy = modifiedBy ?? "system"
        };

        await _context.Documents.AddAsync(document, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken); // assigns document.Id

        // The previous version is retained (row + file) but no longer the
        // current version of this document.
        current.SupersededById = document.Id;
        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return ToDto(document);
    }

    /// <summary>
    /// Lists every retained version of a document's version family (issue
    /// #23), oldest first. The referenced id may be any version in the
    /// family (current or superseded); soft-deleted versions are hidden
    /// like everywhere else. Unknown, purged, or other-tenant documents
    /// yield a uniform 404.
    /// </summary>
    public async Task<List<DocumentDTO>> GetDocumentVersionsAsync(int id, int tenantId, CancellationToken cancellationToken = default)
    {
        var entry = await VisibleDocuments(tenantId)
            .Include(d => d.Links)
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == id && !d.IsDeleted, cancellationToken);

        if (entry == null)
        {
            // Uniform 404: existence and tenant ownership are not leaked.
            throw new KeyNotFoundException($"Document with id {id} not found in tenant {tenantId}.");
        }

        // Walk only this version family. Libraries have no contract anchor;
        // scanning every standalone file would make history reads unbounded.
        var query = VisibleDocuments(tenantId).AsNoTracking().Include(d => d.Links)
            .Where(d => !d.IsDeleted);
        var current = entry;
        var visited = new HashSet<int> { current.Id };
        while (true)
        {
            var previous = await query.FirstOrDefaultAsync(d => d.SupersededById == current.Id, cancellationToken);
            if (previous is null || !visited.Add(previous.Id)) break;
            current = previous;
        }
        var family = new List<Document>();
        visited.Clear();
        while (visited.Add(current.Id))
        {
            family.Add(current);
            if (current.SupersededById is not int nextId) break;
            var next = await query.FirstOrDefaultAsync(d => d.Id == nextId, cancellationToken);
            if (next is null) break;
            current = next;
        }
        return family.OrderBy(d => d.Version).ThenBy(d => d.UploadedAt).Select(ToDto).ToList();
    }
    /// <summary>
    /// Resolves a document by id in the tenant and walks supersession links
    /// forward to the current version (issue #23), so callers may reference
    /// either the current version or an old one. Uniform 404 for unknown,
    /// soft-deleted, or other-tenant documents.
    /// </summary>
    private async Task<Document> FindCurrentDocumentAsync(int id, int tenantId, CancellationToken cancellationToken)
    {
        var document = await VisibleDocuments(tenantId)
            .Include(d => d.Links)
            .FirstOrDefaultAsync(d => d.Id == id && !d.IsDeleted, cancellationToken);

        if (document == null)
        {
            throw new KeyNotFoundException($"Document with id {id} not found in tenant {tenantId}.");
        }

        var visited = new HashSet<int> { document.Id };
        while (document.SupersededById is int nextId && visited.Add(nextId))
        {
            var next = await VisibleDocuments(tenantId)
            .Include(d => d.Links)
                .FirstOrDefaultAsync(d => d.Id == nextId && !d.IsDeleted, cancellationToken);
            // The current version was purged/retired: stop at the newest
            // still-present version instead of failing.
            if (next == null)
            {
                break;
            }

            document = next;
        }

        return document;
    }

    /// <summary>Strips directory parts from a client-supplied file name.</summary>
    private static string SanitizeFileName(string fileName)
    {
        var safeFileName = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(safeFileName))
        {
            throw new InvalidOperationException("File name must not be empty.");
        }

        return safeFileName;
    }

    public async Task<DocumentDTO> UpdateDocumentAsync(int id, DocumentCreateDTO dto, int tenantId, string? modifiedBy, CancellationToken cancellationToken = default)
    {
        var document = await VisibleDocuments(tenantId)
            .Include(d => d.Links)
            .FirstOrDefaultAsync(d => d.Id == id && !d.IsDeleted, cancellationToken);

        if (document == null)
        {
            throw new KeyNotFoundException($"Document with id {id} not found in tenant {tenantId}.");
        }

        if (dto.FileName != null && !string.IsNullOrWhiteSpace(dto.FileName))
        {
            document.FileName = Path.GetFileName(dto.FileName);
        }
        if (dto.FileType != null && !string.IsNullOrWhiteSpace(dto.FileType))
        {
            document.FileType = dto.FileType;
        }
        document.ModifiedBy = modifiedBy ?? "system";
        document.ModifiedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);

        return ToDto(document);
    }

    public async Task<DocumentDTO> UpdateMetadataAsync(int id, DocumentMetadataDTO dto, int tenantId, string? modifiedBy, CancellationToken cancellationToken = default)
    {
        ValidateMetadata(null, null, dto.Title, dto.Description, dto.Category);
        var document = await VisibleDocuments(tenantId)
            .Include(d => d.Links)
            .FirstOrDefaultAsync(d => d.Id == id && !d.IsDeleted, cancellationToken);

        if (document == null)
        {
            throw new KeyNotFoundException($"Document with id {id} not found in tenant {tenantId}.");
        }

        // Both fields are optional: null = leave as-is. An explicit blank
        // title clears it (the UI then falls back to the file name).
        if (dto.Title is not null)
        {
            document.Title = NormalizeTitle(dto.Title);
        }
        if (dto.Description is not null) document.Description = NormalizeTitle(dto.Description);
        if (dto.Category is not null) document.Category = NormalizeTitle(dto.Category);
        if (dto.DocumentDate is not null)
        {
            document.DocumentDate = dto.DocumentDate;
        }
        document.ModifiedBy = modifiedBy ?? "system";
        document.ModifiedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);

        return ToDto(document);
    }

    /// <summary>Trims a user-supplied title; blank becomes null.</summary>
    private static string? NormalizeTitle(string? title) =>
        string.IsNullOrWhiteSpace(title) ? null : title.Trim();

    public async Task<bool> DeleteDocumentAsync(int id, int tenantId, string? modifiedBy, CancellationToken cancellationToken = default)
    {
        await using var transaction = await _context.BeginSerializableTransactionAsync(cancellationToken);
        var document = await VisibleDocuments(tenantId)
            .Include(d => d.Links)
            .FirstOrDefaultAsync(d => d.Id == id && !d.IsDeleted, cancellationToken);

        if (document == null)
        {
            return false;
        }

        // Soft delete (issue #14): the row and the stored file are kept so
        // the document can be recovered; the retention purge (deletion
        // queue) permanently removes them after the tenant's retention
        // period.
        if (await _context.Invoices.AnyAsync(i => i.DocumentId == id && !i.IsDeleted, cancellationToken))
            throw new InvalidOperationException("This document is evidence for an active invoice. Remove that reference or archive the invoice first.");
        document.IsDeleted = true;
        document.DeletedAt = DateTime.UtcNow;
        document.ModifiedBy = modifiedBy ?? "system";
        document.ModifiedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return true;

    }

    /// <summary>
    /// Lists the tenant's soft-deleted documents that are still in their
    /// retention period (issue #42) — the data behind the trash view.
    /// Documents of other tenants and documents already purged are not
    /// returned.
    /// </summary>
    public async Task<List<DocumentDTO>> GetDeletedDocumentsAsync(int tenantId, CancellationToken cancellationToken = default)
    {
        var evidenceIds = await _context.Invoices.Where(i => i.TenantId == tenantId && i.DocumentId != null)
            .Select(i => i.DocumentId!.Value).Distinct().ToListAsync(cancellationToken);
        var documents = await VisibleDocuments(tenantId)
            .Include(d => d.Links)
            .AsNoTracking()
            .Where(d => d.IsDeleted && d.DeletedAt != null)
            .Include(d => d.Tenant)
            .Include(d => d.Contract).ThenInclude(c => c!.Tenant)
            .ToListAsync(cancellationToken);

        // Retention resolution (TenantSettingsParser) is not translatable to
        // SQL, so filter/sort/project in memory — the trash list is small by
        // construction (one tenant's in-retention soft deletes).
        return documents
            .OrderByDescending(d => d.DeletedAt)
            .Select(d =>
            {
                var retentionDays =
                    TenantSettingsParser.GetDocumentRetentionDays(d.Tenant?.Settings ?? d.Contract?.Tenant?.Settings)
                    ?? _retentionOptions.DefaultDays;
                var dto = ToDto(d);
                dto.DeletedAt = d.DeletedAt;
                dto.RetainedForInvoice = evidenceIds.Contains(d.Id);
                dto.ExpiresAt = dto.RetainedForInvoice ? null : d.DeletedAt!.Value.AddDays(retentionDays);
                return dto;
            })
            .ToList();
    }

    /// <summary>
    /// Restores a soft-deleted document (issue #42): clears IsDeleted/
    /// DeletedAt so it reappears in the contract's document list. The stored
    /// file is untouched — soft delete never removed it. Throws
    /// KeyNotFoundException (404) for purged, unknown, or other-tenant
    /// documents, and for documents that were never soft-deleted.
    /// </summary>
    public async Task<DocumentDTO> RestoreDocumentAsync(int id, int tenantId, string? modifiedBy, CancellationToken cancellationToken = default)
    {
        var document = await VisibleDocuments(tenantId)
            .Include(d => d.Links)
            .Include(d => d.Tenant)
            .Include(d => d.Contract).ThenInclude(c => c!.Tenant)
            .FirstOrDefaultAsync(d => d.Id == id && d.IsDeleted, cancellationToken);

        if (document == null)
        {
            // Uniform 404: existence and tenant ownership are not leaked.
            throw new KeyNotFoundException($"Document with id {id} not found in tenant {tenantId}.");
        }

        document.IsDeleted = false;
        document.DeletedAt = null;
        document.ModifiedBy = modifiedBy ?? "system";
        document.ModifiedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);

        return ToDto(document);
    }

    public async Task<byte[]> GetDocumentContentAsync(int id, int tenantId, CancellationToken cancellationToken = default)
    {
        var document = await VisibleDocuments(tenantId)
            .Include(d => d.Links)
            .FirstOrDefaultAsync(d => d.Id == id && !d.IsDeleted, cancellationToken);

        if (document == null)
        {
            throw new KeyNotFoundException($"Document with id {id} not found in tenant {tenantId}.");
        }

        try
        {
            return await _fileStorage.ReadAsync(document.FilePath, cancellationToken);
        }
        catch (FileNotFoundException)
        {
            // Metadata exists but the stored file is gone.
            throw new KeyNotFoundException($"Document with id {id} not found in tenant {tenantId}.");
        }
    }

    public async Task<DocumentDTO> SetContractLinkAsync(int id, int contractId, int tenantId, bool linked, string? actor, CancellationToken cancellationToken = default)
    {
        await using var transaction = await _context.BeginSerializableTransactionAsync(cancellationToken);
        var document = await FindCurrentDocumentAsync(id, tenantId, cancellationToken);
        if (document.Scope != (tenantId == 0 ? DocumentScope.Personal : DocumentScope.Tenant))
            throw new InvalidOperationException("Personal files cannot be linked to shared contracts.");
        if (!await _context.Contracts.VisibleIn(tenantId, _identity?.UserId).AnyAsync(c => c.Id == contractId && c.DeletedAt == null, cancellationToken))
            throw new KeyNotFoundException("Contract not found.");
        if (document.ContractId == contractId && !linked)
            throw new InvalidOperationException("The original contract is the storage anchor and cannot be unlinked. You can move the document to trash instead.");
        var existing = document.Links.SingleOrDefault(l => l.ContractId == contractId);
        if (linked && document.ContractId != contractId && existing is null)
            document.Links.Add(new DocumentLink { ContractId = contractId, CreatedBy = actor, ModifiedBy = actor });
        if (!linked && existing is not null)
        {
            if (await _context.Invoices.AnyAsync(i => i.DocumentId == document.Id && i.ContractId == contractId && !i.IsDeleted, cancellationToken))
                throw new InvalidOperationException("This link is used by an active invoice. Remove that invoice reference first.");
            _context.DocumentLinks.Remove(existing);
            document.Links.Remove(existing);
        }
        document.ModifiedBy = actor;
        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ToDto(document);
    }

    private IQueryable<Document> VisibleDocuments(int tenantId)
    {
        var userId = _identity?.UserId;
        return _context.Documents.Where(d => d.ContractId == null || d.Contract!.DeletedAt == null || d.Links.Any(l => l.Contract.DeletedAt == null) || _context.Invoices.Any(i => i.DocumentId == d.Id)).Where(d => (tenantId > 0 && d.Scope == DocumentScope.Tenant
                && (d.TenantId == tenantId || (d.TenantId == null && d.Contract != null && d.Contract.TenantId == tenantId)))
            || (d.Scope == DocumentScope.Personal && userId != null && d.UserId == userId));
    }

    public async Task<List<DocumentDTO>> GetLibraryAsync(int tenantId, DocumentScope scope, bool deleted, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(scope) || page < 1 || pageSize < 1 || pageSize > 200 || (page - 1) > int.MaxValue / pageSize)
            throw new InvalidOperationException("Invalid scope or pagination.");
        var documents = await VisibleDocuments(tenantId).AsNoTracking().Include(d => d.Links)
            .Include(d => d.Tenant).Include(d => d.Contract).ThenInclude(c => c!.Tenant)
            .Where(d => d.Scope == scope && d.IsDeleted == deleted && (deleted || d.SupersededById == null))
            .OrderByDescending(d => d.ModifiedAt).ThenBy(d => d.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        var result = documents.Select(ToDto).ToList();
        if (deleted)
        {
            var evidence = await _context.Invoices.Where(i => i.TenantId == tenantId && i.DocumentId != null)
                .Select(i => i.DocumentId!.Value).ToListAsync(cancellationToken);
            foreach (var dto in result)
            {
                var document = documents.Single(d => d.Id == dto.Id);
                dto.DeletedAt = document.DeletedAt;
                dto.RetainedForInvoice = evidence.Contains(dto.Id);
                dto.ExpiresAt = dto.RetainedForInvoice ? null : document.DeletedAt?.AddDays(
                    TenantSettingsParser.GetDocumentRetentionDays(document.Tenant?.Settings ?? document.Contract?.Tenant?.Settings) ?? _retentionOptions.DefaultDays);
            }
        }
        return result;
    }

    private static void ValidateMetadata(string? name, string? type, string? title, string? description, string? category)
    {
        if (name is not null && (string.IsNullOrWhiteSpace(name) || name.Length > 255)
            || type is not null && (string.IsNullOrWhiteSpace(type) || type.Length > 200)
            || title?.Length > 255 || description?.Length > 2000 || category?.Length > 100)
            throw new InvalidOperationException("Document metadata is empty or exceeds its permitted length.");
    }

    private static DocumentDTO ToDto(Document d) => new()
    {
        Id = d.Id,
        Title = d.Title,
        FileName = d.FileName,
        FileType = d.FileType,
        FileSize = d.FileSize,
        ContractId = d.ContractId,
        TenantId = d.TenantId,
        UserId = d.UserId,
        Scope = d.Scope.ToString(),
        Description = d.Description,
        Category = d.Category,
        ContractIds = d.Links.Select(l => l.ContractId).Concat(d.ContractId.HasValue ? new[] { d.ContractId.Value } : Array.Empty<int>()).Distinct().ToList(),
        UploadedAt = d.UploadedAt,
        DocumentDate = d.DocumentDate,
        CreatedAt = d.CreatedAt,
        ModifiedAt = d.ModifiedAt,
        Version = d.Version,
        CreatedBy = d.CreatedBy,
        ModifiedBy = d.ModifiedBy
    };
}
