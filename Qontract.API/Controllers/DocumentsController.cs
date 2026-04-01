using Qontract.Domain.Models;
using Qontract.Application.Models;
using Qontract.Application.Services;

namespace Qontract.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DocumentsController : ControllerBase
{
    private readonly IDocumentService _documentService;

    public DocumentsController(IDocumentService documentService)
    {
        _documentService = documentService;
    }

    [HttpGet("contract/{contractId}/documents")]
    public async Task<ActionResult<List<DocumentDTO>>> GetDocuments(int contractId)
    {
        var documents = await _documentService.GetDocumentsAsync(contractId);
        return Ok(documents);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<DocumentDTO>> GetDocument(int id)
    {
        var document = await _documentService.GetDocumentByIdAsync(id);
        return Ok(document);
    }

    [HttpPost("contract/{contractId}/documents")]
    public async Task<ActionResult<DocumentDTO>> CreateDocument(int contractId, [FromBody] DocumentCreateDTO dto)
    {
        var document = await _documentService.CreateDocumentAsync(dto, contractId, 1, User.Identity?.Name);
        return CreatedAtAction(nameof(GetDocument), new { id = document.Id }, document);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<DocumentDTO>> UpdateDocument(int id, [FromBody] DocumentCreateDTO dto)
    {
        var document = await _documentService.UpdateDocumentAsync(id, dto, 1, User.Identity?.Name);
        return Ok(document);
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult<bool>> DeleteDocument(int id)
    {
        var result = await _documentService.DeleteDocumentAsync(id, 1, User.Identity?.Name);
        return Ok(result);
    }
}
