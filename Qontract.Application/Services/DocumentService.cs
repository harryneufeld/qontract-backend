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

    public DocumentService(IApplicationDbContext context, IFileStorage fileStorage, IOptions<DocumentRetentionOptions> retentionOptions)
    {
        _context = context;
        _fileStorage = fileStorage;
        _retentionOptions = retentionOptions.Value;
    }

    public async Task<List<DocumentDTO>> GetDocumentsAsync(int contractId, int tenantId, CancellationToken cancellationToken = default)
    {
        // Contract must belong to the tenant (KeyNotFound -> 404 via middleware).
        var contract = await _context.Contracts
            .FirstOrDefaultAsync(c => c.Id == contractId && c.TenantId == tenantId, cancellationToken);
        if (contract == null)
        {
            throw new KeyNotFoundException($"Contract with id {contractId} not found in tenant {tenantId}.");
        }

        // Only the current version of each logical document (issue #23):
        // superseded re-upload versions are listed via
        // GET /api/documents/{id}/versions, not here.
        var documents = await _context.Documents
            .Include(d => d.Links)
            .Where(d => d.Contract.TenantId == tenantId && (d.ContractId == contractId || d.Links.Any(l => l.ContractId == contractId)) && !d.IsDeleted && d.SupersededById == null)
            .OrderByDescending(d => d.ModifiedAt)
            .Select(d => new DocumentDTO
            {
                Id = d.Id,
                Title = d.Title,
                FileName = d.FileName,
                FileType = d.FileType,
                FileSize = d.FileSize,
                ContractId = d.ContractId,
                ContractIds = d.Links.Select(l => l.ContractId).ToList(),
                UploadedAt = d.UploadedAt,
                DocumentDate = d.DocumentDate,
                CreatedAt = d.CreatedAt,
                ModifiedAt = d.ModifiedAt,
                Version = d.Version,
                CreatedBy = d.CreatedBy,
                ModifiedBy = d.ModifiedBy
            })
            .ToListAsync(cancellationToken);

        foreach (var dto in documents) dto.ContractIds = dto.ContractIds.Append(dto.ContractId).Distinct().ToList();
        return documents;
    }

    public async Task<DocumentDTO> GetDocumentByIdAsync(int id, int tenantId, CancellationToken cancellationToken = default)
    {
        var document = await _context.Documents
            .Include(d => d.Links)
            .Where(d => d.Id == id && !d.IsDeleted)
            .Select(d => new DocumentDTO
            {
                Id = d.Id,
                Title = d.Title,
                FileName = d.FileName,
                FileType = d.FileType,
                FileSize = d.FileSize,
                ContractId = d.ContractId,
                ContractIds = d.Links.Select(l => l.ContractId).ToList(),
                UploadedAt = d.UploadedAt,
                DocumentDate = d.DocumentDate,
                CreatedAt = d.CreatedAt,
                ModifiedAt = d.ModifiedAt,
                Version = d.Version,
                CreatedBy = d.CreatedBy,
                ModifiedBy = d.ModifiedBy
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (document == null || await IsContractInOtherTenantAsync(document.ContractId, tenantId, cancellationToken))
        {
            // Uniform 404: existence and tenant ownership are not leaked.
            throw new KeyNotFoundException($"Document with id {id} not found in tenant {tenantId}.");
        }

        document.ContractIds = document.ContractIds.Append(document.ContractId).Distinct().ToList();
        return document;
    }

    public async Task<DocumentDTO> CreateDocumentAsync(DocumentCreateDTO dto, int contractId, int tenantId, string? createdBy, CancellationToken cancellationToken = default)
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

        // Contract must exist and belong to the tenant.
        var contract = await _context.Contracts
            .FirstOrDefaultAsync(c => c.Id == contractId && c.TenantId == tenantId, cancellationToken);
        if (contract == null)
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
        var filePath = _fileStorage.GenerateFilePath(contractId, Guid.NewGuid().ToString("N") + ".bin", 1);
        await _fileStorage.SaveAsync(fileBytes, filePath, cancellationToken);

        var document = new Document
        {
            FileName = safeFileName,
            FileType = dto.FileType,
            FilePath = filePath,
            FileSize = fileBytes.Length, // trust the actual bytes, not the client's claim
            UploadedBy = createdBy ?? "system",
            ContractId = contractId,
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
        var filePath = _fileStorage.GenerateFilePath(current.ContractId, Guid.NewGuid().ToString("N") + ".bin", newVersion);
        await _fileStorage.SaveAsync(fileBytes, filePath, cancellationToken);

        var document = new Document
        {
            FileName = fileName,
            FileType = fileType,
            FilePath = filePath,
            FileSize = fileBytes.Length, // trust the actual bytes, not the client's claim
            UploadedBy = modifiedBy ?? "system",
            ContractId = current.ContractId,
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
        var entry = await _context.Documents
            .Include(d => d.Links)
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == id && !d.IsDeleted, cancellationToken);

        if (entry == null || await IsContractInOtherTenantAsync(entry.ContractId, tenantId, cancellationToken))
        {
            // Uniform 404: existence and tenant ownership are not leaked.
            throw new KeyNotFoundException($"Document with id {id} not found in tenant {tenantId}.");
        }

        // All versions of one family share the contract and version families
        // are small, so walk the supersession chains in memory.
        var contractDocuments = await _context.Documents
            .Include(d => d.Links)
            .AsNoTracking()
            .Where(d => d.ContractId == entry.ContractId && !d.IsDeleted)
            .ToListAsync(cancellationToken);

        var byId = contractDocuments.ToDictionary(d => d.Id);
        var supersededBy = contractDocuments
            .Where(d => d.SupersededById is not null)
            .GroupBy(d => d.SupersededById!.Value)
            .ToDictionary(g => g.Key, g => g.First());

        // Forward to the current (unsuperseded) version...
        var current = entry;
        var hops = 0;
        while (current.SupersededById is int nextId
            && byId.TryGetValue(nextId, out var next)
            && hops++ < 100)
        {
            current = next;
        }

        // ...then back through the chain to the first version.
        var family = new List<Document> { current };
        while (supersededBy.TryGetValue(current.Id, out var previous))
        {
            family.Add(previous);
            current = previous;
        }

        return family
            .OrderBy(d => d.Version)
            .ThenBy(d => d.UploadedAt)
            .Select(ToDto)
            .ToList();
    }

    /// <summary>
    /// Resolves a document by id in the tenant and walks supersession links
    /// forward to the current version (issue #23), so callers may reference
    /// either the current version or an old one. Uniform 404 for unknown,
    /// soft-deleted, or other-tenant documents.
    /// </summary>
    private async Task<Document> FindCurrentDocumentAsync(int id, int tenantId, CancellationToken cancellationToken)
    {
        var document = await _context.Documents
            .Include(d => d.Links)
            .FirstOrDefaultAsync(d => d.Id == id && !d.IsDeleted, cancellationToken);

        if (document == null || await IsContractInOtherTenantAsync(document.ContractId, tenantId, cancellationToken))
        {
            throw new KeyNotFoundException($"Document with id {id} not found in tenant {tenantId}.");
        }

        var hops = 0;
        while (document.SupersededById is int nextId && hops++ < 100)
        {
            var next = await _context.Documents
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
        var document = await _context.Documents
            .Include(d => d.Links)
            .FirstOrDefaultAsync(d => d.Id == id && !d.IsDeleted, cancellationToken);

        if (document == null || await IsContractInOtherTenantAsync(document.ContractId, tenantId, cancellationToken))
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
        var document = await _context.Documents
            .Include(d => d.Links)
            .FirstOrDefaultAsync(d => d.Id == id && !d.IsDeleted, cancellationToken);

        if (document == null || await IsContractInOtherTenantAsync(document.ContractId, tenantId, cancellationToken))
        {
            throw new KeyNotFoundException($"Document with id {id} not found in tenant {tenantId}.");
        }

        // Both fields are optional: null = leave as-is. An explicit blank
        // title clears it (the UI then falls back to the file name).
        if (dto.Title is not null)
        {
            document.Title = NormalizeTitle(dto.Title);
        }
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
        var document = await _context.Documents
            .Include(d => d.Links)
            .FirstOrDefaultAsync(d => d.Id == id && !d.IsDeleted, cancellationToken);

        if (document == null || await IsContractInOtherTenantAsync(document.ContractId, tenantId, cancellationToken))
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
        var documents = await _context.Documents
            .Include(d => d.Links)
            .AsNoTracking()
            .Where(d => d.IsDeleted && d.DeletedAt != null)
            .Include(d => d.Contract)
            .ThenInclude(c => c!.Tenant)
            .ToListAsync(cancellationToken);

        // Retention resolution (TenantSettingsParser) is not translatable to
        // SQL, so filter/sort/project in memory — the trash list is small by
        // construction (one tenant's in-retention soft deletes).
        return documents
            .Where(d => d.Contract != null && d.Contract.TenantId == tenantId)
            .OrderByDescending(d => d.DeletedAt)
            .Select(d =>
            {
                var retentionDays =
                    TenantSettingsParser.GetDocumentRetentionDays(d.Contract!.Tenant?.Settings)
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
        var document = await _context.Documents
            .Include(d => d.Links)
            .Include(d => d.Contract)
            .ThenInclude(c => c!.Tenant)
            .FirstOrDefaultAsync(d => d.Id == id && d.IsDeleted, cancellationToken);

        if (document == null || document.Contract == null || document.Contract.TenantId != tenantId)
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
        var document = await _context.Documents
            .Include(d => d.Links)
            .FirstOrDefaultAsync(d => d.Id == id && !d.IsDeleted, cancellationToken);

        if (document == null || await IsContractInOtherTenantAsync(document.ContractId, tenantId, cancellationToken))
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

    /// <summary>
    /// True when the document's contract belongs to a different tenant (or no
    /// longer exists) — i.e. the current tenant must not see this document.
    /// </summary>
    private async Task<bool> IsContractInOtherTenantAsync(int contractId, int tenantId, CancellationToken cancellationToken)
    {
        var contract = await _context.Contracts
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == contractId, cancellationToken);
        return contract == null || contract.TenantId != tenantId;
    }

    public async Task<DocumentDTO> SetContractLinkAsync(int id, int contractId, int tenantId, bool linked, string? actor, CancellationToken cancellationToken = default)
    {
        await using var transaction = await _context.BeginSerializableTransactionAsync(cancellationToken);
        var document = await FindCurrentDocumentAsync(id, tenantId, cancellationToken);
        if (!await _context.Contracts.AnyAsync(c => c.Id == contractId && c.TenantId == tenantId, cancellationToken))
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

    private static DocumentDTO ToDto(Document d) => new()
    {
        Id = d.Id,
        Title = d.Title,
        FileName = d.FileName,
        FileType = d.FileType,
        FileSize = d.FileSize,
        ContractId = d.ContractId,
        ContractIds = d.Links.Select(l => l.ContractId).Append(d.ContractId).Distinct().ToList(),
        UploadedAt = d.UploadedAt,
        DocumentDate = d.DocumentDate,
        CreatedAt = d.CreatedAt,
        ModifiedAt = d.ModifiedAt,
        Version = d.Version,
        CreatedBy = d.CreatedBy,
        ModifiedBy = d.ModifiedBy
    };
}
