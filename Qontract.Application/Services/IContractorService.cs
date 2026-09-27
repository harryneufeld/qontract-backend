using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Qontract.Application.Models;

namespace Qontract.Application.Services;

public interface IContractorService
{
    Task<List<ContractorDTO>> GetContractorsAsync(int tenantId, CancellationToken cancellationToken = default);
    Task<ContractorDTO> GetContractorByIdAsync(int id, int tenantId, CancellationToken cancellationToken = default);
    Task<ContractorDTO> CreateContractorAsync(ContractorCreateDTO dto, string? createdBy, CancellationToken cancellationToken = default);
    Task<ContractorDTO> UpdateContractorAsync(int id, int tenantId, ContractorUpdateDTO dto, string? modifiedBy, CancellationToken cancellationToken = default);
    Task<ContractorDTO> DeactivateContractorAsync(int id, int tenantId, string? modifiedBy, CancellationToken cancellationToken = default);
    Task<ContractorDTO> ActivateContractorAsync(int id, int tenantId, string? modifiedBy, CancellationToken cancellationToken = default);
}
