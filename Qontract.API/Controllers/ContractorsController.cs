using Qontract.Domain.Models;
using Qontract.Application.Models;
using Qontract.Application.Services;

namespace Qontract.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ContractorsController : ControllerBase
{
    private readonly IContractorService _contractorService;

    public ContractorsController(IContractorService contractorService)
    {
        _contractorService = contractorService;
    }

    [HttpGet]
    public async Task<ActionResult<List<ContractorDTO>>> GetContractors()
    {
        var contractors = await _contractorService.GetContractorsAsync(1); // TODO: get from auth context
        return Ok(contractors);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ContractorDTO>> GetContractor(int id)
    {
        var contractor = await _contractorService.GetContractorByIdAsync(id, 1);
        return Ok(contractor);
    }

    [HttpPost]
    public async Task<ActionResult<ContractorDTO>> CreateContractor([FromBody] ContractorCreateDTO dto)
    {
        var contractor = await _contractorService.CreateContractorAsync(dto, User.Identity?.Name);
        return CreatedAtAction(nameof(GetContractor), new { id = contractor.Id }, contractor);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<ContractorDTO>> UpdateContractor(int id, [FromBody] ContractorUpdateDTO dto)
    {
        var contractor = await _contractorService.UpdateContractorAsync(id, 1, dto, User.Identity?.Name);
        return Ok(contractor);
    }

    [HttpPatch("{id}/deactivate")]
    public async Task<ActionResult<ContractorDTO>> DeactivateContractor(int id)
    {
        var contractor = await _contractorService.DeactivateContractorAsync(id, 1, User.Identity?.Name);
        return Ok(contractor);
    }
}
