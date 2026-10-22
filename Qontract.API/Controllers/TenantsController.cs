using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Qontract.Domain.Models;
using Qontract.Application.Models;
using Qontract.Application.Services;

namespace Qontract.API.Controllers;

[ApiController]
// Tenant management (list, read, update, deactivate) is an admin-only
// operation (issue #24). Creating a workspace is open to every signed-in
// user ("Session" policy: no workspace needed), who becomes its
// administrator. Each action declares its own authorization.
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Route("api/[controller]")]
public class TenantsController : ControllerBase
{
    private readonly ITenantService _tenantService;
    private readonly IValidator<TenantCreateDTO> _createValidator;
    private readonly IValidator<TenantUpdateDTO> _updateValidator;

    public TenantsController(
        ITenantService tenantService,
        IValidator<TenantCreateDTO> createValidator,
        IValidator<TenantUpdateDTO> updateValidator)
    {
        _tenantService = tenantService;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
    }

    /// <summary>
    /// Identity used for audit fields (CreatedBy/ModifiedBy). The JWT carries
    /// the email claim, not name (same convention as the contractor controller).
    /// </summary>
    private string? CurrentUser =>
        User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value
        ?? User.FindFirst("email")?.Value
        ?? User.Identity?.Name;

    private int CurrentUserId => int.Parse(
        User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
        ?? throw new UnauthorizedAccessException());

    [HttpGet]
    [Authorize(Roles = AppRoles.TenantAdmin)]
    public async Task<ActionResult<List<TenantDTO>>> GetTenants()
    {
        var tenants = await _tenantService.GetTenantsAsync(CurrentUserId);
        return Ok(tenants);
    }

    [HttpGet("{id}")]
    [Authorize(Roles = AppRoles.TenantAdmin)]
    public async Task<ActionResult<TenantDTO>> GetTenant(int id)
    {
        var tenant = await _tenantService.GetTenantByIdAsync(id, CurrentUserId);
        return Ok(tenant);
    }

    [HttpPost]
    [Authorize(Policy = "Session")]
    public async Task<ActionResult<TenantDTO>> CreateTenant([FromBody] TenantCreateDTO dto)
    {
        var reject = await DtoValidation.RejectIfInvalidAsync(_createValidator, dto);
        if (reject is not null)
            return reject;

        var tenant = await _tenantService.CreateTenantAsync(dto, CurrentUserId, CurrentUser);
        return CreatedAtAction(nameof(GetTenant), new { id = tenant.Id }, tenant);
    }

    [HttpPut("{id}")]
    [Authorize(Roles = AppRoles.TenantAdmin)]
    public async Task<ActionResult<TenantDTO>> UpdateTenant(int id, [FromBody] TenantUpdateDTO dto)
    {
        var reject = await DtoValidation.RejectIfInvalidAsync(_updateValidator, dto);
        if (reject is not null)
            return reject;

        var tenant = await _tenantService.UpdateTenantAsync(id, CurrentUserId, dto, CurrentUser);
        return Ok(tenant);
    }

    [HttpPost("{id}/apply-default-currency")]
    [Authorize(Roles = AppRoles.TenantAdmin)]
    public async Task<ActionResult> ApplyDefaultCurrency(int id, [FromBody] ApplyTenantCurrencyDTO dto)
    {
        var changed = await _tenantService.ApplyDefaultCurrencyAsync(id, CurrentUserId,
            dto.ReplaceExisting, CurrentUser);
        return Ok(new { changedContracts = changed });
    }

    [HttpPatch("{id}/deactivate")]
    [Authorize(Roles = AppRoles.TenantAdmin)]
    public async Task<ActionResult<TenantDTO>> DeactivateTenant(int id)
    {
        var tenant = await _tenantService.DeactivateTenantAsync(id, CurrentUserId, CurrentUser);
        return Ok(tenant);
    }
}
