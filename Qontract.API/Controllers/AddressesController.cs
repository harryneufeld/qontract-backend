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

    public AddressesController(IAddressService addressService)
    {
        _addressService = addressService;
    }

    [HttpPost]
    public async Task<ActionResult<AddressDTO>> CreateAddress([FromBody] AddressCreateDTO dto)
    {
        var address = await _addressService.CreateAddressAsync(dto, 0, dto.EntityType, User.Identity?.Name);
        return CreatedAtAction(nameof(GetAddress), new { id = address.Id }, address);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<AddressDTO>> UpdateAddress(int id, [FromBody] AddressCreateDTO dto)
    {
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
        var address = await _addressService.DeleteAddressAsync(id, User.Identity?.Name);
        return Ok(address);
    }
}
