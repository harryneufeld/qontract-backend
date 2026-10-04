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
public class AddressesController : ControllerBase
{
    private readonly IAddressService _addressService;
    private readonly IValidator<AddressCreateDTO> _createValidator;
    private readonly IValidator<AddressUpdateDTO> _updateValidator;

    public AddressesController(
        IAddressService addressService,
        IValidator<AddressCreateDTO> createValidator,
        IValidator<AddressUpdateDTO> updateValidator)
    {
        _addressService = addressService;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
    }

    /// <summary>
    /// The tenant a request applies to, resolved from the authenticated user's
    /// TenantId claim (same convention as the other controllers).
    /// </summary>
    private int CurrentTenantId =>
        int.Parse(User.FindFirst("TenantId")?.Value
            ?? throw new InvalidOperationException("Authenticated user has no TenantId claim."));

    private int CurrentUserId => int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);

    [HttpGet("me")]
    public async Task<ActionResult<List<AddressDTO>>> GetMyAddresses() =>
        Ok(await _addressService.GetMyAddressesAsync());

    [HttpGet("tenant")]
    public async Task<ActionResult<List<AddressDTO>>> GetTenantAddresses() =>
        Ok(await _addressService.GetTenantAddressesAsync(CurrentTenantId));

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

        // Account and tenant ownership come only from authenticated identity.
        // Contract/contractor references must belong to the active workspace.
        var (entityId, entityType) = dto.EntityType switch
        {
            EntityType.Tenant => (CurrentTenantId, EntityType.Tenant),
            EntityType.Person => (CurrentUserId, EntityType.Person),
            EntityType.Contractor => (dto.ContractorId ?? 0, EntityType.Contractor),
            EntityType.Contract => (dto.ContractId ?? 0, EntityType.Contract),
            _ => (0, dto.EntityType)
        };

        var address = await _addressService.CreateAddressAsync(dto, entityId, entityType, CurrentTenantId, CurrentUser);
        return CreatedAtAction(nameof(GetAddress), new { id = address.Id }, address);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<AddressDTO>> UpdateAddress(int id, [FromBody] AddressUpdateDTO dto)
    {
        var existing = await _addressService.GetAddressByIdAsync(id, CurrentTenantId);
        // Unchanged historical values may exceed today's input limits. Preserve
        // them verbatim while validating every newly entered value.
        if (dto.Street == existing.Street) dto.Street = null;
        if (dto.HouseNumber == existing.HouseNumber) dto.HouseNumber = null;
        if (dto.City == existing.City) dto.City = null;
        if (dto.Zip == existing.Zip) dto.Zip = null;
        if (dto.Country == existing.Country) dto.Country = null;
        var reject = await DtoValidation.RejectIfInvalidAsync(_updateValidator, dto);
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
