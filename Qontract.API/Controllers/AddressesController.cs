using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Qontract.Domain.Models;
using Qontract.Application.Models;
using Qontract.Application.Services;

namespace Qontract.API.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public class AddressesController : ControllerBase
{
    private readonly IAddressService _addressService;
    private readonly IValidator<AddressCreateDTO> _createValidator;

    public AddressesController(
        IAddressService addressService,
        IValidator<AddressCreateDTO> createValidator)
    {
        _addressService = addressService;
        _createValidator = createValidator;
    }

    /// <summary>
    /// The tenant a request applies to, resolved from the authenticated user's
    /// TenantId claim (same convention as the other controllers).
    /// </summary>
    private int CurrentTenantId =>
        int.Parse(User.FindFirst("TenantId")?.Value
            ?? throw new InvalidOperationException("Authenticated user has no TenantId claim."));

    /// <summary>
    /// Identity used for audit fields (CreatedBy/ModifiedBy). The JWT carries
    /// the email claim, not name (same convention as the contractor controller).
    /// </summary>
    private string? CurrentUser =>
        User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value
        ?? User.FindFirst("email")?.Value
        ?? User.Identity?.Name;

    [HttpPost]
    public async Task<ActionResult<AddressDTO>> CreateAddress([FromBody] AddressCreateDTO dto)
    {
        var reject = await DtoValidation.RejectIfInvalidAsync(_createValidator, dto);
        if (reject is not null)
            return reject;

        // Never trust the client for the owning entity (issue #32): the
        // caller's own address is the tenant's; a contractor address is
        // explicitly passed and must belong to the caller's tenant
        // (enforced by the service, 404 otherwise).
        var (entityId, entityType) = dto.EntityType switch
        {
            EntityType.Tenant => (CurrentTenantId, EntityType.Tenant),
            EntityType.Contractor => (dto.ContractorId ?? 0, EntityType.Contractor),
            _ => (0, dto.EntityType)
        };

        var address = await _addressService.CreateAddressAsync(dto, entityId, entityType, CurrentTenantId, CurrentUser);
        return CreatedAtAction(nameof(GetAddress), new { id = address.Id }, address);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<AddressDTO>> UpdateAddress(int id, [FromBody] AddressCreateDTO dto)
    {
        var reject = await DtoValidation.RejectIfInvalidAsync(_createValidator, dto);
        if (reject is not null)
            return reject;

        var address = await _addressService.UpdateAddressAsync(id, CurrentTenantId, dto, CurrentUser);
        return Ok(address);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<AddressDTO>> GetAddress(int id)
    {
        var address = await _addressService.GetAddressByIdAsync(id, CurrentTenantId);
        return Ok(address);
    }

    [HttpPatch("{id}/deactivate")]
    public async Task<ActionResult<AddressDTO>> DeactivateAddress(int id)
    {
        var address = await _addressService.DeactivateAddressAsync(id, CurrentTenantId, CurrentUser);
        return Ok(address);
    }
}
