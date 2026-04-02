using Microsoft.EntityFrameworkCore;
using Qontract.Domain.Models;
using Qontract.Application.Models;
using Qontract.Application.Interfaces;
using Microsoft.Extensions.Logging;
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

    public async Task<List<DocumentDTO>> GetDocumentsAsync(int contractId, CancellationToken cancellationToken = default)
    {
        var documents = await _context.Documents
            .Where(d => d.ContractId == contractId)
            .OrderByDescending(d => d.UpdatedAt)
            .Select(d => new DocumentDTO
            {
                Id = d.Id,
                FileName = d.FileName,
                FileType = d.FileType,
                FilePath = d.FilePath,
                FileSize = d.FileSize,
                ContractId = d.ContractId,
                UploadedAt = d.UploadedAt,
                CreatedAt = d.CreatedAt,
                UpdatedAt = d.UpdatedAt,
                Version = d.Version,
                CreatedBy = d.CreatedBy,
                ModifiedBy = d.ModifiedBy
            })
            .ToListAsync(cancellationToken);

        return documents;
    }

    public async Task<DocumentDTO> GetDocumentByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var document = await _context.Documents
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

        if (document == null)
            throw new KeyNotFoundException($"Document with ID {id} not found.");

        return new DocumentDTO
        {
            Id = document.Id,
            FileName = document.FileName,
            FileType = document.FileType,
            FilePath = document.FilePath,
            FileSize = document.FileSize,
            ContractId = document.ContractId,
            UploadedAt = document.UploadedAt,
            CreatedAt = document.CreatedAt,
            UpdatedAt = document.UpdatedAt,
            Version = document.Version,
            CreatedBy = document.CreatedBy,
            ModifiedBy = document.ModifiedBy
        };
    }

    public async Task<DocumentDTO> CreateDocumentAsync(DocumentCreateDTO dto, int contractId, int uploadedBy, string? createdBy, CancellationToken cancellationToken = default)
    {
        var fileBytes = await _fileStorage.UploadAsync(dto.FileName, dto.FileType, dto.FileSize);

        if (fileBytes == null)
            throw new InvalidOperationException($"Failed to upload file: {dto.FileName}");

        // Check for existing version
        var maxVersion = await _context.Documents
            .Where(d => d.ContractId == contractId)
            .MaxAsync(d => (int?)d.Version, cancellationToken) ?? 0;
        var nextVersion = maxVersion + 1;

        var document = new Document
        {
            FileName = dto.FileName,
            FileType = dto.FileType,
            FilePath = _fileStorage.GenerateFilePath(contractId, dto.FileName, nextVersion + 1),
            FileSize = dto.FileSize,
            ContractId = contractId,
            UploadedBy = uploadedBy.ToString(),
            CreatedBy = createdBy ?? string.Empty,
            Version = nextVersion + 1,
            UploadedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _context.Documents.Add(document);
        await _context.SaveChangesAsync(cancellationToken);

        return await GetDocumentByIdAsync(document.Id, cancellationToken);
    }

    public async Task<DocumentDTO> UpdateDocumentAsync(int id, DocumentCreateDTO dto, int modifiedBy, string? modifiedByName, CancellationToken cancellationToken = default)
    {
        var document = await _context.Documents
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

        if (document == null)
            throw new KeyNotFoundException($"Document with ID {id} not found.");

        var fileBytes = await _fileStorage.UploadAsync(dto.FileName, dto.FileType, dto.FileSize);

        if (fileBytes == null)
            throw new InvalidOperationException($"Failed to upload file: {dto.FileName}");

        document.FileName = dto.FileName;
        document.FileType = dto.FileType;
        document.FilePath = _fileStorage.GenerateFilePath(document.ContractId, dto.FileName, document.Version + 1);
        document.FileSize = dto.FileSize;
        document.UpdatedAt = DateTime.UtcNow;
        if (!string.IsNullOrEmpty(modifiedByName)) document.ModifiedBy = modifiedByName;
        document.Version += 1;

        await _context.SaveChangesAsync(cancellationToken);

        return await GetDocumentByIdAsync(document.Id, cancellationToken);
    }

    public async Task<bool> DeleteDocumentAsync(int id, int modifiedBy, string? modifiedByName, CancellationToken cancellationToken = default)
    {
        var document = await _context.Documents
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

        if (document == null)
            throw new KeyNotFoundException($"Document with ID {id} not found.");

        // Delete file from storage
        try
        {
            await _fileStorage.DeleteAsync(document.FilePath);
        }
        catch { /* File might not exist, ignore */ }

        document.FileName = string.Empty;
        document.FilePath = string.Empty;
        document.FileSize = 0;

        await _context.SaveChangesAsync(cancellationToken);

        return true;
    }

    public async Task<byte[]> GetDocumentContentAsync(int id, CancellationToken cancellationToken = default)
    {
        var document = await _context.Documents
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

        if (document == null)
            throw new KeyNotFoundException($"Document with ID {id} not found.");

        var bytes = await _fileStorage.ReadAsync(document.FilePath);
        return bytes;
    }
}
