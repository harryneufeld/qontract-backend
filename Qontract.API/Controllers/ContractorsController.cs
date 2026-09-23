using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Qontract.Application.Models;
using Qontract.Application.Services;

namespace Qontract.API.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public class ContractorsController : ControllerBase
{
    private readonly IContractorService _contractorService;
    private readonly IAddressService _addressService;
    private readonly IValidator<ContractorCreateDTO> _createValidator;
    private readonly IValidator<ContractorUpdateDTO> _updateValidator;

    public ContractorsController(
        IContractorService contractorService,
        IAddressService addressService,
        IValidator<ContractorCreateDTO> createValidator,
        IValidator<ContractorUpdateDTO> updateValidator)
    {
        _contractorService = contractorService;
        _addressService = addressService;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
    }

    /// <summary>
    /// The tenant a request applies to, resolved from the authenticated user's
    /// TenantId claim. All contractor data is scoped to this tenant.
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
    public async Task<ActionResult<List<ContractorDTO>>> GetContractors()
    {
        var contractors = await _contractorService.GetContractorsAsync(CurrentTenantId);
        return Ok(contractors);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ContractorDTO>> GetContractor(int id)
    {
        // KeyNotFoundException -> 404 is handled uniformly by
        // ExceptionHandlingMiddleware (issue #11).
        var contractor = await _contractorService.GetContractorByIdAsync(id, CurrentTenantId);
        return Ok(contractor);
    }

    /// <summary>
    /// Active addresses of a contractor (issue #41). The contractor must
    /// belong to the caller's tenant — cross-tenant and unknown ids both 404.
    /// </summary>
    [HttpGet("{id:int}/addresses")]
    public async Task<ActionResult<List<AddressDTO>>> GetContractorAddresses(int id)
    {
        // KeyNotFoundException -> 404 is handled uniformly by
        // ExceptionHandlingMiddleware (issue #11).
        var addresses = await _addressService.GetAddressesForContractorAsync(id, CurrentTenantId);
        return Ok(addresses);
    }

    [HttpPost]
    public async Task<ActionResult<ContractorDTO>> CreateContractor([FromBody] ContractorCreateDTO dto)
    {
        var reject = await DtoValidation.RejectIfInvalidAsync(_createValidator, dto);
        if (reject is not null)
            return reject;

        // Always scope to the authenticated tenant, never trust the client.
        dto.TenantId = CurrentTenantId;

        var contractor = await _contractorService.CreateContractorAsync(dto, CurrentUser);
        return CreatedAtAction(nameof(GetContractor), new { id = contractor.Id }, contractor);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<ContractorDTO>> UpdateContractor(int id, [FromBody] ContractorUpdateDTO dto)
    {
        var reject = await DtoValidation.RejectIfInvalidAsync(_updateValidator, dto);
        if (reject is not null)
            return reject;

        // KeyNotFoundException -> 404 is handled uniformly by
        // ExceptionHandlingMiddleware (issue #11).
        var contractor = await _contractorService.UpdateContractorAsync(id, CurrentTenantId, dto, CurrentUser);
        return Ok(contractor);
    }

    [HttpPatch("{id:int}/deactivate")]
    public async Task<ActionResult<ContractorDTO>> DeactivateContractor(int id)
    {
        try
        {
            var contractor = await _contractorService.DeactivateContractorAsync(id, CurrentTenantId, CurrentUser);
            return Ok(contractor);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }
}
