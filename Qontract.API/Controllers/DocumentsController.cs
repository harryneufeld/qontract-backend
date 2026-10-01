using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Qontract.Application.Models;
using Qontract.Application.Services;

namespace Qontract.API.Controllers;

[ApiController]
[Authorize]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Route("api/[controller]")]
public class DocumentsController : ControllerBase
{
    private readonly IDocumentService _documentService;
    private readonly IValidator<DocumentCreateDTO> _createValidator;
    private readonly IValidator<DocumentMetadataDTO> _metadataValidator;
    private readonly IValidator<DocumentReuploadDTO> _reuploadValidator;

    public DocumentsController(
        IDocumentService documentService,
        IValidator<DocumentCreateDTO> createValidator,
        IValidator<DocumentMetadataDTO> metadataValidator,
        IValidator<DocumentReuploadDTO> reuploadValidator)
    {
        _documentService = documentService;
        _createValidator = createValidator;
        _metadataValidator = metadataValidator;
        _reuploadValidator = reuploadValidator;
    }

    /// <summary>
    /// The tenant a request applies to, resolved from the authenticated user's
    /// TenantId claim. All document data is scoped to this tenant.
    /// </summary>
    private int CurrentTenantId =>
        int.Parse(User.FindFirst("TenantId")?.Value
            ?? throw new InvalidOperationException("Authenticated user has no TenantId claim."));

    /// <summary>
    /// Identity used for audit fields (CreatedBy/ModifiedBy). The JWT carries
    /// the email claim, not name.
    /// </summary>
    private string? CurrentUser =>
        User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value
        ?? User.FindFirst("email")?.Value
        ?? User.Identity?.Name;

    /// <summary>GET /api/documents/contract/{contractId}/documents</summary>
    [HttpGet("contract/{contractId}/documents")]
    public async Task<ActionResult<List<DocumentDTO>>> GetDocuments(int contractId)
    {
        var documents = await _documentService.GetDocumentsAsync(contractId, CurrentTenantId);
        return Ok(documents);
    }

    /// <summary>GET /api/documents/{id}</summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<DocumentDTO>> GetDocument(int id)
    {
        var document = await _documentService.GetDocumentByIdAsync(id, CurrentTenantId);
        return Ok(document);
    }

    /// <summary>
    /// GET /api/documents/{id}/download — raw file bytes with the original
    /// file name and content type so clients can save/preview it.
    /// </summary>
    [HttpGet("{id}/download")]
    public async Task<IActionResult> DownloadDocument(int id)
    {
        var document = await _documentService.GetDocumentByIdAsync(id, CurrentTenantId);
        var bytes = await _documentService.GetDocumentContentAsync(id, CurrentTenantId);

        var contentType = string.IsNullOrEmpty(document.FileType)
            ? "application/octet-stream"
            : document.FileType;
        return File(bytes, contentType, document.FileName);
    }

    /// <summary>POST /api/documents/contract/{contractId}/documents</summary>
    [HttpPost("contract/{contractId}/documents")]
    public async Task<ActionResult<DocumentDTO>> CreateDocument(int contractId, [FromBody] DocumentCreateDTO dto)
    {
        // The DTO carries a contractId from the client; the route value wins.
        dto.ContractId = contractId;
        var reject = await DtoValidation.RejectIfInvalidAsync(_createValidator, dto);
        if (reject is not null)
            return reject;

        var document = await _documentService.CreateDocumentAsync(dto, contractId, CurrentTenantId, CurrentUser);
        return CreatedAtAction(nameof(GetDocument), new { id = document.Id }, document);
    }

    /// <summary>GET /api/documents/trash — soft-deleted (in-retention) documents</summary>
    [HttpGet("trash")]
    public async Task<ActionResult<List<DocumentDTO>>> GetDeletedDocuments()
    {
        var documents = await _documentService.GetDeletedDocumentsAsync(CurrentTenantId);
        return Ok(documents);
    }

    /// <summary>POST /api/documents/{id}/restore — undo a soft delete</summary>
    [HttpPost("{id}/restore")]
    public async Task<ActionResult<DocumentDTO>> RestoreDocument(int id)
    {
        var document = await _documentService.RestoreDocumentAsync(id, CurrentTenantId, CurrentUser);
        return Ok(document);
    }

    /// <summary>
    /// GET /api/documents/{id}/versions — the full version history of the
    /// document's family (issue #23), oldest first. Every entry keeps its
    /// own id, so old versions stay individually downloadable.
    /// </summary>
    [HttpGet("{id}/versions")]
    public async Task<ActionResult<List<DocumentDTO>>> GetDocumentVersions(int id)
    {
        var versions = await _documentService.GetDocumentVersionsAsync(id, CurrentTenantId);
        return Ok(versions);
    }

    /// <summary>
    /// POST /api/documents/{id}/reupload — stores a new version of the
    /// document (issue #23). The previous version is retained (listable via
    /// GET /api/documents/{id}/versions, downloadable by its own id) and the
    /// new version increments the version number. Ids of superseded versions
    /// are accepted and resolved to the current version.
    /// </summary>
    [HttpPost("{id}/reupload")]
    public async Task<ActionResult<DocumentDTO>> ReuploadDocument(int id, [FromBody] DocumentReuploadDTO dto)
    {
        var reject = await DtoValidation.RejectIfInvalidAsync(_reuploadValidator, dto);
        if (reject is not null)
            return reject;

        var document = await _documentService.ReuploadDocumentAsync(id, dto, CurrentTenantId, CurrentUser);
        return Ok(document);
    }

    /// <summary>PUT /api/documents/{id}</summary>
    [HttpPut("{id}")]
    public async Task<ActionResult<DocumentDTO>> UpdateDocument(int id, [FromBody] DocumentCreateDTO dto)
    {
        // The update path only applies FileName/FileType, but the payload
        // contract is the same full DTO as create (matching the previous
        // DataAnnotations behaviour), so the same validator applies.
        var reject = await DtoValidation.RejectIfInvalidAsync(_createValidator, dto);
        if (reject is not null)
            return reject;

        var document = await _documentService.UpdateDocumentAsync(id, dto, CurrentTenantId, CurrentUser);
        return Ok(document);
    }

    /// <summary>
    /// PATCH /api/documents/{id}/metadata — user-editable title and
    /// document date (app issue #5). Null fields are left untouched.
    /// </summary>
    [HttpPatch("{id}/metadata")]
    public async Task<ActionResult<DocumentDTO>> UpdateDocumentMetadata(int id, [FromBody] DocumentMetadataDTO dto)
    {
        var reject = await DtoValidation.RejectIfInvalidAsync(_metadataValidator, dto);
        if (reject is not null)
            return reject;

        var document = await _documentService.UpdateMetadataAsync(id, dto, CurrentTenantId, CurrentUser);
        return Ok(document);
    }

    /// <summary>DELETE /api/documents/{id}</summary>
    [HttpDelete("{id}")]
    public async Task<ActionResult<bool>> DeleteDocument(int id)
    {
        var result = await _documentService.DeleteDocumentAsync(id, CurrentTenantId, CurrentUser);
        if (!result)
        {
            // Uniform 404 via the middleware.
            throw new KeyNotFoundException($"Document with id {id} not found in tenant {CurrentTenantId}.");
        }
        return Ok(result);
    }

    [HttpPost("{id}/contracts/{contractId}")]
    public async Task<ActionResult<DocumentDTO>> LinkContract(int id, int contractId) =>
        Ok(await _documentService.SetContractLinkAsync(id, contractId, CurrentTenantId, true, CurrentUser));

    [HttpDelete("{id}/contracts/{contractId}")]
    public async Task<ActionResult<DocumentDTO>> UnlinkContract(int id, int contractId) =>
        Ok(await _documentService.SetContractLinkAsync(id, contractId, CurrentTenantId, false, CurrentUser));
}
