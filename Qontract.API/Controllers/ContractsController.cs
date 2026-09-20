using Microsoft.AspNetCore.Authorization;
using Qontract.Domain.Models;
using Qontract.Application.Models;
using Qontract.Application.Services;

namespace Qontract.API.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public class ContractsController : ControllerBase
{
    private readonly IContractService _contractService;

    public ContractsController(IContractService contractService)
    {
        _contractService = contractService;
    }

    /// <summary>
    /// The tenant a request applies to, resolved from the authenticated user's
    /// TenantId claim. All contract data is scoped to this tenant.
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

    [HttpGet]
    public async Task<ActionResult<List<ContractDTO>>> GetContracts()
    {
        var contracts = await _contractService.GetContractsAsync(CurrentTenantId);
        return Ok(contracts);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ContractDTO>> GetContract(int id)
    {
        // KeyNotFoundException -> 404 is handled uniformly by
        // ExceptionHandlingMiddleware (issue #11).
        var contract = await _contractService.GetContractByIdAsync(id, CurrentTenantId);
        return Ok(contract);
    }

    [HttpPost]
    public async Task<ActionResult<ContractDTO>> CreateContract([FromBody] ContractCreateDTO dto)
    {
        var contract = await _contractService.CreateContractAsync(CurrentTenantId, dto, CurrentUser);
        return CreatedAtAction(nameof(GetContract), new { id = contract.Id }, contract);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<ContractDTO>> UpdateContract(int id, [FromBody] ContractUpdateDTO dto)
    {
        var contract = await _contractService.UpdateContractAsync(id, CurrentTenantId, dto, CurrentUser);
        return Ok(contract);
    }

    [HttpPatch("{id}/deactivate")]
    public async Task<ActionResult<ContractDTO>> DeactivateContract(int id)
    {
        var contract = await _contractService.DeactivateContractAsync(id, CurrentTenantId, CurrentUser);
        return Ok(contract);
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult<bool>> DeleteContract(int id)
    {
        var result = await _contractService.DeleteContractSoftAsync(id, CurrentTenantId, CurrentUser);
        return Ok(result);
    }
}
