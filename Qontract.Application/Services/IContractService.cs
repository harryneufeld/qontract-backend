using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Qontract.Application.Models;

namespace Qontract.Application.Services;

public interface IContractService
{
    Task<List<ContractDTO>> GetContractsAsync(int tenantId, CancellationToken cancellationToken = default);
    Task<ContractDTO> GetContractByIdAsync(int id, int tenantId, CancellationToken cancellationToken = default);
    Task<ContractDTO> CreateContractAsync(int tenantId, ContractCreateDTO dto, string? createdBy, CancellationToken cancellationToken = default);
    Task<ContractDTO> UpdateContractAsync(int id, int tenantId, ContractUpdateDTO dto, string? modifiedBy, CancellationToken cancellationToken = default);
    Task<ContractDTO> DeactivateContractAsync(int id, int tenantId, string? modifiedBy, CancellationToken cancellationToken = default);
    Task<bool> DeleteContractSoftAsync(int id, int tenantId, string? modifiedBy, CancellationToken cancellationToken = default);
}
