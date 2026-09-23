using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Qontract.Application.Models;

namespace Qontract.Application.Services;

public interface ITenantService
{
    Task<List<TenantDTO>> GetTenantsAsync(CancellationToken cancellationToken = default);
    Task<TenantDTO> GetTenantByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<TenantDTO> CreateTenantAsync(TenantCreateDTO dto, string? createdBy, CancellationToken cancellationToken = default);
    Task<TenantDTO> UpdateTenantAsync(int id, TenantUpdateDTO dto, string? modifiedBy, CancellationToken cancellationToken = default);
    Task<TenantDTO> DeactivateTenantAsync(int id, string? modifiedBy, CancellationToken cancellationToken = default);
}
