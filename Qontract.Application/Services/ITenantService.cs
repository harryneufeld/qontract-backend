using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Qontract.Application.Models;

namespace Qontract.Application.Services;

public interface ITenantService
{
    Task<List<TenantDTO>> GetTenantsAsync(int userId, CancellationToken cancellationToken = default);
    Task<TenantDTO> GetTenantByIdAsync(int id, int userId, CancellationToken cancellationToken = default);
    Task<TenantDTO> CreateTenantAsync(TenantCreateDTO dto, int userId, int currentTenantId, string? createdBy, CancellationToken cancellationToken = default);
    Task<TenantDTO> UpdateTenantAsync(int id, int userId, TenantUpdateDTO dto, string? modifiedBy, CancellationToken cancellationToken = default);
    Task<TenantDTO> DeactivateTenantAsync(int id, int userId, string? modifiedBy, CancellationToken cancellationToken = default);
}
