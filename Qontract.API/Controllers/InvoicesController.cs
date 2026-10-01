using Microsoft.AspNetCore.Authorization;
using Qontract.Application.Models;
using Qontract.Application.Services;

namespace Qontract.API.Controllers;

[ApiController, Authorize, ApiVersion("1.0")]
[Route("api/invoices"), Route("api/v{version:apiVersion}/invoices")]
public class InvoicesController(InvoiceService service) : ControllerBase
{
    private int TenantId => int.Parse(User.FindFirst("TenantId")!.Value);
    private string? Actor => User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value;
    [HttpGet]
    public async Task<IActionResult> List(int? contractId, bool deleted = false, int page = 1, int pageSize = 50) =>
        Ok(await service.List(TenantId, contractId, deleted, page, pageSize));
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id) => Ok(await service.Get(id, TenantId));
    [HttpPost]
    public async Task<IActionResult> Create(InvoiceInput input)
    {
        var result = await service.Save(null, input, TenantId, Actor);
        return CreatedAtAction(nameof(Get), new { id = result.Id }, result);
    }
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, InvoiceInput input) => Ok(await service.Save(id, input, TenantId, Actor));
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id) => Ok(await service.SetDeleted(id, TenantId, true, Actor));
    [HttpPost("{id:guid}/restore")]
    public async Task<IActionResult> Restore(Guid id) => Ok(await service.SetDeleted(id, TenantId, false, Actor));
}
