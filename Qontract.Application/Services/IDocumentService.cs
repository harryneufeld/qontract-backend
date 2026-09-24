using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Qontract.Application.Models;

namespace Qontract.Application.Services;

public interface IDocumentService
{
    Task<List<DocumentDTO>> GetDocumentsAsync(int contractId, int tenantId, CancellationToken cancellationToken = default);
    Task<DocumentDTO> GetDocumentByIdAsync(int id, int tenantId, CancellationToken cancellationToken = default);
    Task<DocumentDTO> CreateDocumentAsync(DocumentCreateDTO dto, int contractId, int tenantId, string? createdBy, CancellationToken cancellationToken = default);
    Task<DocumentDTO> ReuploadDocumentAsync(int id, DocumentReuploadDTO dto, int tenantId, string? modifiedBy, CancellationToken cancellationToken = default);
    Task<List<DocumentDTO>> GetDocumentVersionsAsync(int id, int tenantId, CancellationToken cancellationToken = default);
    Task<DocumentDTO> UpdateDocumentAsync(int id, DocumentCreateDTO dto, int tenantId, string? modifiedBy, CancellationToken cancellationToken = default);
    Task<DocumentDTO> UpdateMetadataAsync(int id, DocumentMetadataDTO dto, int tenantId, string? modifiedBy, CancellationToken cancellationToken = default);
    Task<bool> DeleteDocumentAsync(int id, int tenantId, string? modifiedBy, CancellationToken cancellationToken = default);
    Task<List<DocumentDTO>> GetDeletedDocumentsAsync(int tenantId, CancellationToken cancellationToken = default);
    Task<DocumentDTO> RestoreDocumentAsync(int id, int tenantId, string? modifiedBy, CancellationToken cancellationToken = default);
    Task<byte[]> GetDocumentContentAsync(int id, int tenantId, CancellationToken cancellationToken = default);
}
