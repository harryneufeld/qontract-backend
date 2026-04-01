using Qontract.Domain.Models;
using Qontract.Application.Models;
using Qontract.Application.Services;

namespace Qontract.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ContractsController : ControllerBase
{
    private readonly IContractService _contractService;

    public ContractsController(IContractService contractService)
    {
        _contractService = contractService;
    }

    [HttpGet]
    public async Task<ActionResult<List<ContractDTO>>> GetContracts()
    {
        var contracts = await _contractService.GetContractsAsync();
        return Ok(contracts);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ContractDTO>> GetContract(int id)
    {
        var contract = await _contractService.GetContractByIdAsync(id);
        return Ok(contract);
    }

    [HttpPost]
    public async Task<ActionResult<ContractDTO>> CreateContract([FromBody] ContractCreateDTO dto)
    {
        var contract = await _contractService.CreateContractAsync(dto, User.Identity?.Name);
        return CreatedAtAction(nameof(GetContract), new { id = contract.Id }, contract);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<ContractDTO>> UpdateContract(int id, [FromBody] ContractUpdateDTO dto)
    {
        var contract = await _contractService.UpdateContractAsync(id, dto, User.Identity?.Name);
        return Ok(contract);
    }

    [HttpPatch("{id}/deactivate")]
    public async Task<ActionResult<ContractDTO>> DeactivateContract(int id)
    {
        var contract = await _contractService.DeactivateContractAsync(id, User.Identity?.Name);
        return Ok(contract);
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult<bool>> DeleteContract(int id)
    {
        var result = await _contractService.DeleteContractSoftAsync(id, User.Identity?.Name);
        return Ok(result);
    }
}
