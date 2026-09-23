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

    [HttpPost]
    public async Task<ActionResult<AddressDTO>> CreateAddress([FromBody] AddressCreateDTO dto)
    {
        var reject = await DtoValidation.RejectIfInvalidAsync(_createValidator, dto);
        if (reject is not null)
            return reject;

        var entityId = dto.EntityType == EntityType.Tenant ? CurrentTenantId : 0;
        var address = await _addressService.CreateAddressAsync(dto, entityId, dto.EntityType, User.Identity?.Name);
        return CreatedAtAction(nameof(GetAddress), new { id = address.Id }, address);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<AddressDTO>> UpdateAddress(int id, [FromBody] AddressCreateDTO dto)
    {
        var reject = await DtoValidation.RejectIfInvalidAsync(_createValidator, dto);
        if (reject is not null)
            return reject;

        var address = await _addressService.UpdateAddressAsync(id, dto, User.Identity?.Name);
        return Ok(address);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<AddressDTO>> GetAddress(int id)
    {
        var address = await _addressService.GetAddressByIdAsync(id);
        return Ok(address);
    }

    [HttpPatch("{id}/deactivate")]
    public async Task<ActionResult<AddressDTO>> DeactivateAddress(int id)
    {
        var address = await _addressService.DeactivateAddressAsync(id, User.Identity?.Name);
        return Ok(address);
    }
}
