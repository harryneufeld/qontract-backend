namespace Qontract.Application.Services;

public interface IDocumentService
{
    Task<List<DocumentDTO>> GetDocumentsAsync(int contractId, CancellationToken cancellationToken = default);
    Task<DocumentDTO> GetDocumentByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<DocumentDTO> CreateDocumentAsync(DocumentCreateDTO dto, int contractId, int uploadedBy, string? createdBy, CancellationToken cancellationToken = default);
    Task<DocumentDTO> UpdateDocumentAsync(int id, DocumentCreateDTO dto, int modifiedBy, string? modifiedBy, CancellationToken cancellationToken = default);
    Task<bool> DeleteDocumentAsync(int id, int modifiedBy, string? modifiedBy, CancellationToken cancellationToken = default);
    Task<byte[]> GetDocumentContentAsync(int id, CancellationToken cancellationToken = default);
}
