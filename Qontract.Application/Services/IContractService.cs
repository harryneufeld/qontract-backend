using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Qontract.Application.Models;

namespace Qontract.Application.Services;

public interface IContractService
{
    Task<List<ContractDTO>> GetContractsAsync(CancellationToken cancellationToken = default);
    Task<ContractDTO> GetContractByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<ContractDTO> CreateContractAsync(ContractCreateDTO dto, string? createdBy, CancellationToken cancellationToken = default);
    Task<ContractDTO> UpdateContractAsync(int id, ContractUpdateDTO dto, string? modifiedBy, CancellationToken cancellationToken = default);
    Task<ContractDTO> DeactivateContractAsync(int id, string? modifiedBy, CancellationToken cancellationToken = default);
    Task<bool> DeleteContractSoftAsync(int id, string? modifiedBy, CancellationToken cancellationToken = default);
}
