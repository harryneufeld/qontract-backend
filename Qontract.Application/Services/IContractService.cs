using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Qontract.Application.Models;

namespace Qontract.Application.Services;

public interface IContractService
{
    Task<string> GetDefaultCurrencyAsync(int tenantId, CancellationToken cancellationToken = default);
    Task<List<ContractDTO>> GetContractsAsync(int tenantId, int page = 0, int pageSize = 0, string? status = null, CancellationToken cancellationToken = default, string scope = "tenant", bool trash = false);
    Task<ContractStatsDTO> GetStatsAsync(int tenantId, CancellationToken cancellationToken = default, string scope = "mine");
    Task<ContractDTO> RestoreContractAsync(int id, int tenantId, string? actor, CancellationToken cancellationToken = default);
    Task<List<ResponsibleMemberDTO>> GetResponsibleMembersAsync(int tenantId, CancellationToken cancellationToken = default);
    Task<ContractDTO> GetContractByIdAsync(int id, int tenantId, CancellationToken cancellationToken = default);
    Task<ContractDTO> CreateContractAsync(int tenantId, ContractCreateDTO dto, string? createdBy, CancellationToken cancellationToken = default);
    Task<ContractDTO> UpdateContractAsync(int id, int tenantId, ContractUpdateDTO dto, string? modifiedBy, CancellationToken cancellationToken = default);
    Task<ContractDTO> DeactivateContractAsync(int id, int tenantId, string? modifiedBy, CancellationToken cancellationToken = default);
    Task<bool> DeleteContractSoftAsync(int id, int tenantId, string? modifiedBy, CancellationToken cancellationToken = default);
}
