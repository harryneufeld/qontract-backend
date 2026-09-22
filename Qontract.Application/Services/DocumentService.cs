using Microsoft.EntityFrameworkCore;
using Qontract.Domain.Models;
using Qontract.Application.Models;
using Qontract.Core.Interfaces;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace Qontract.Application.Services;

public class DocumentService : IDocumentService
{
    private readonly IApplicationDbContext _context;
    private readonly IFileStorage _fileStorage;
    private readonly int _maxFileSize = 50 * 1024 * 1024; // 50MB

    public DocumentService(IApplicationDbContext context, IFileStorage fileStorage)
    {
        _context = context;
        _fileStorage = fileStorage;
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

        var documents = await _context.Documents
            .Where(d => d.ContractId == contractId)
            .OrderByDescending(d => d.ModifiedAt)
            .Select(d => new DocumentDTO
            {
                Id = d.Id,
                FileName = d.FileName,
                FileType = d.FileType,
                FileSize = d.FileSize,
                ContractId = d.ContractId,
                UploadedAt = d.UploadedAt,
                CreatedAt = d.CreatedAt,
                ModifiedAt = d.ModifiedAt,
                Version = d.Version,
                CreatedBy = d.CreatedBy,
                ModifiedBy = d.ModifiedBy
            })
            .ToListAsync(cancellationToken);

        return documents;
    }

    public async Task<DocumentDTO> GetDocumentByIdAsync(int id, int tenantId, CancellationToken cancellationToken = default)
    {
        var document = await _context.Documents
            .Where(d => d.Id == id)
            .Select(d => new DocumentDTO
            {
                Id = d.Id,
                FileName = d.FileName,
                FileType = d.FileType,
                FileSize = d.FileSize,
                ContractId = d.ContractId,
                UploadedAt = d.UploadedAt,
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

        if (fileBytes.Length > _maxFileSize)
        {
            throw new InvalidOperationException($"File size exceeds maximum allowed size of {_maxFileSize / (1024 * 1024)}MB.");
        }

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

        // Version 1 for a fresh upload; later versions overwrite the file
        // at the same path (kept for API compatibility with the old layout).
        var filePath = _fileStorage.GenerateFilePath(contractId, safeFileName, 1);
        await File.WriteAllBytesAsync(filePath, fileBytes);

        var document = new Document
        {
            FileName = safeFileName,
            FileType = dto.FileType,
            FilePath = filePath,
            FileSize = fileBytes.Length, // trust the actual bytes, not the client's claim
            UploadedBy = createdBy ?? "system",
            ContractId = contractId,
            UploadedAt = DateTime.UtcNow,
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

    public async Task<DocumentDTO> UpdateDocumentAsync(int id, DocumentCreateDTO dto, int tenantId, string? modifiedBy, CancellationToken cancellationToken = default)
    {
        var document = await _context.Documents
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

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

    public async Task<bool> DeleteDocumentAsync(int id, int tenantId, string? modifiedBy, CancellationToken cancellationToken = default)
    {
        var document = await _context.Documents
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

        if (document == null || await IsContractInOtherTenantAsync(document.ContractId, tenantId, cancellationToken))
        {
            return false;
        }

        // Best-effort: remove the stored file so disk space is reclaimed.
        try
        {
            await _fileStorage.DeleteAsync(document.FilePath);
        }
        catch
        {
            // File already gone (or unreadable path) — the DB row is what
            // matters; deletion still succeeds.
        }

        _context.Documents.Remove(document);
        await _context.SaveChangesAsync(cancellationToken);

        return true;
    }

    public async Task<byte[]> GetDocumentContentAsync(int id, int tenantId, CancellationToken cancellationToken = default)
    {
        var document = await _context.Documents
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

        if (document == null || await IsContractInOtherTenantAsync(document.ContractId, tenantId, cancellationToken))
        {
            throw new KeyNotFoundException($"Document with id {id} not found in tenant {tenantId}.");
        }

        try
        {
            return await File.ReadAllBytesAsync(document.FilePath);
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

    private static DocumentDTO ToDto(Document d) => new()
    {
        Id = d.Id,
        FileName = d.FileName,
        FileType = d.FileType,
        FileSize = d.FileSize,
        ContractId = d.ContractId,
        UploadedAt = d.UploadedAt,
        CreatedAt = d.CreatedAt,
        ModifiedAt = d.ModifiedAt,
        Version = d.Version,
        CreatedBy = d.CreatedBy,
        ModifiedBy = d.ModifiedBy
    };
}
