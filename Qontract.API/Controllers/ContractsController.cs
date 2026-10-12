using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Qontract.Domain.Models;
using Qontract.Application.Models;
using Qontract.Application.Services;

namespace Qontract.API.Controllers;

[ApiController]
[Authorize(Policy = "Account")]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Route("api/[controller]")]
public class ContractsController : ControllerBase
{
    private readonly IContractService _contractService;
    private readonly IValidator<ContractCreateDTO> _createValidator;
    private readonly IValidator<ContractUpdateDTO> _updateValidator;

    public ContractsController(
        IContractService contractService,
        IValidator<ContractCreateDTO> createValidator,
        IValidator<ContractUpdateDTO> updateValidator)
    {
        _contractService = contractService;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
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
    public async Task<ActionResult<List<ContractDTO>>> GetContracts(
        [FromQuery] int page = 0,
        [FromQuery] int pageSize = 0,
        [FromQuery] string? status = null, [FromQuery] string scope = "tenant", [FromQuery] bool trash = false)
    {
        // Offset paging for the dashboard (issue #43). Absent/zero params
        // preserve the historic full-list behaviour.
        var p = page < 0 ? 0 : page;
        var s = pageSize < 0 ? 0 : pageSize;

        // Dashboard status buckets (app#36): the list is restricted to one
        // of the stat-chip buckets so a filtered view covers ALL of the
        // tenant's matching contracts, not only the loaded page.
        if (status is not null &&
            status is not ("active" or "inactive" or "expiring" or "expired" or "minDate"))
        {
            return BadRequest(new
            {
                error = $"Unknown status filter '{status}'. Expected one of: active, expiring, expired, minDate."
            });
        }

        var contracts = await _contractService.GetContractsAsync(
            CurrentTenantId, p, s, status, scope: scope, trash: trash);
        return Ok(contracts);
    }

    /// <summary>
    /// Tenant-wide dashboard counters (app#33). The list above is paginated
    /// (issue #43), so the stat chips need totals the client cannot derive
    /// from the loaded page. Declared before the {id} route so the literal
    /// wins.
    /// </summary>
    [HttpGet("default-currency")]
    public async Task<ActionResult> GetDefaultCurrency() =>
        Ok(new { currency = await _contractService.GetDefaultCurrencyAsync(CurrentTenantId) });

    [HttpGet("stats")]
    public async Task<ActionResult<ContractStatsDTO>> GetStats([FromQuery] string scope = "mine")
    {
        var stats = await _contractService.GetStatsAsync(CurrentTenantId, scope: scope);
        return Ok(stats);
    }

    [HttpGet("members")]
    public async Task<ActionResult<List<ResponsibleMemberDTO>>> Members() =>
        Ok(await _contractService.GetResponsibleMembersAsync(CurrentTenantId));

    [HttpPost("{id}/restore")]
    public async Task<ActionResult<ContractDTO>> Restore(int id) =>
        Ok(await _contractService.RestoreContractAsync(id, CurrentTenantId, CurrentUser));

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
        var reject = await DtoValidation.RejectIfInvalidAsync(_createValidator, dto);
        if (reject is not null)
            return reject;

        var contract = await _contractService.CreateContractAsync(CurrentTenantId, dto, CurrentUser);
        return CreatedAtAction(nameof(GetContract), new { id = contract.Id }, contract);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<ContractDTO>> UpdateContract(int id, [FromBody] ContractUpdateDTO dto)
    {
        var reject = await DtoValidation.RejectIfInvalidAsync(_updateValidator, dto);
        if (reject is not null)
            return reject;

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
