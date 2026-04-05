using Microsoft.EntityFrameworkCore;
using Qontract.Domain.Models;
using Qontract.Application.Models;
using Qontract.Core.Interfaces;
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
            .Where(d => d.Id == id)
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
            .FirstOrDefaultAsync(cancellationToken);

        if (document == null)
        {
            throw new InvalidOperationException($"Document with id {id} not found");
        }

        return document;
    }

    public async Task<DocumentDTO> CreateDocumentAsync(DocumentCreateDTO dto, int contractId, int uploadedBy, string? createdBy, CancellationToken cancellationToken = default)
    {
        if (dto.FileSize > _maxFileSize)
        {
            throw new InvalidOperationException($"File size exceeds maximum allowed size of {_maxFileSize / (1024 * 1024)}MB");
        }

        var contract = await _context.Contracts
            .FirstOrDefaultAsync(c => c.Id == contractId, cancellationToken);

        if (contract == null)
        {
            throw new InvalidOperationException($"Contract with id {contractId} not found");
        }

        var filePath = _fileStorage.GenerateFilePath(dto.ContractId, dto.FileName, 1);
        var fileBytes = Convert.FromBase64String(dto.FileData);
        await File.WriteAllBytesAsync(filePath, fileBytes);

        var document = new Document
        {
            FileName = dto.FileName,
            FileType = dto.FileType,
            FilePath = filePath,
            FileSize = dto.FileSize,
            ContractId = dto.ContractId,
            UploadedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Version = 1,
            CreatedBy = createdBy ?? "system",
            ModifiedBy = createdBy ?? "system"
        };

        await _context.Documents.AddAsync(document, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

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

    public async Task<DocumentDTO> UpdateDocumentAsync(int id, DocumentCreateDTO dto, int modifiedById, string? modifiedBy, CancellationToken cancellationToken = default)
    {
        var document = await _context.Documents
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

        if (document == null)
        {
            throw new InvalidOperationException($"Document with id {id} not found");
        }

        document.FileName = dto.FileName ?? document.FileName;
        document.FileType = dto.FileType ?? document.FileType;
        document.ModifiedBy = modifiedBy ?? "system";
        document.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);

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

    public async Task<bool> DeleteDocumentAsync(int id, int modifiedById, string? modifiedBy, CancellationToken cancellationToken = default)
    {
        var document = await _context.Documents
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

        if (document == null)
        {
            return false;
        }

        _context.Documents.Remove(document);
        await _context.SaveChangesAsync(cancellationToken);

        return true;
    }

    public async Task<byte[]> GetDocumentContentAsync(int id, CancellationToken cancellationToken = default)
    {
        var document = await _context.Documents
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

        if (document == null)
        {
            throw new InvalidOperationException($"Document with id {id} not found");
        }

        return await File.ReadAllBytesAsync(document.FilePath);
    }
}