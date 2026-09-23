using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Qontract.Application.Models;
using Qontract.Application.Services;

namespace Qontract.API.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public class DocumentsController : ControllerBase
{
    private readonly IDocumentService _documentService;
    private readonly IValidator<DocumentCreateDTO> _createValidator;

    public DocumentsController(
        IDocumentService documentService,
        IValidator<DocumentCreateDTO> createValidator)
    {
        _documentService = documentService;
        _createValidator = createValidator;
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
}
