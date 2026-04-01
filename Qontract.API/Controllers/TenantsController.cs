using Qontract.Domain.Models;
using Qontract.Application.Models;
using Qontract.Application.Services;

namespace Qontract.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TenantsController : ControllerBase
{
    private readonly ITenantService _tenantService;
    private readonly IAuthService _authService;

    public TenantsController(ITenantService tenantService, IAuthService authService)
    {
        _tenantService = tenantService;
        _authService = authService;
    }

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
        var tenant = await _tenantService.CreateTenantAsync(dto);
        return CreatedAtAction(nameof(GetTenant), new { id = tenant.Id }, tenant);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<TenantDTO>> UpdateTenant(int id, [FromBody] TenantUpdateDTO dto)
    {
        var tenant = await _tenantService.UpdateTenantAsync(id, dto);
        return Ok(tenant);
    }

    [HttpPatch("{id}/deactivate")]
    public async Task<ActionResult<TenantDTO>> DeactivateTenant(int id)
    {
        var tenant = await _tenantService.DeactivateTenantAsync(id, User.Identity?.Name);
        return Ok(tenant);
    }
}
