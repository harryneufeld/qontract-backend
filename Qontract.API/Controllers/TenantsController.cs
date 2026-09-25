using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Qontract.Domain.Models;
using Qontract.Application.Models;
using Qontract.Application.Services;

namespace Qontract.API.Controllers;

[ApiController]
// Tenant management (list, read, create, update, deactivate) is an
// admin-only operation (issue #24): only tenant admins may create or
// change tenants.
[Authorize(Roles = AppRoles.TenantAdmin)]
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

    [HttpGet]
    public async Task<ActionResult<List<TenantDTO>>> GetTenants()
    {
        var tenants = await _tenantService.GetTenantsAsync();
        return Ok(tenants);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<TenantDTO>> GetTenant(int id)
    {
        var tenant = await _tenantService.GetTenantByIdAsync(id);
        return Ok(tenant);
    }

    [HttpPost]
    public async Task<ActionResult<TenantDTO>> CreateTenant([FromBody] TenantCreateDTO dto)
    {
        var reject = await DtoValidation.RejectIfInvalidAsync(_createValidator, dto);
        if (reject is not null)
            return reject;

        var tenant = await _tenantService.CreateTenantAsync(dto, CurrentUser);
        return CreatedAtAction(nameof(GetTenant), new { id = tenant.Id }, tenant);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<TenantDTO>> UpdateTenant(int id, [FromBody] TenantUpdateDTO dto)
    {
        var reject = await DtoValidation.RejectIfInvalidAsync(_updateValidator, dto);
        if (reject is not null)
            return reject;

        var tenant = await _tenantService.UpdateTenantAsync(id, dto, CurrentUser);
        return Ok(tenant);
    }

    [HttpPatch("{id}/deactivate")]
    public async Task<ActionResult<TenantDTO>> DeactivateTenant(int id)
    {
        var tenant = await _tenantService.DeactivateTenantAsync(id, CurrentUser);
        return Ok(tenant);
    }
}
