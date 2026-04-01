namespace Qontract.Application.Services;

public interface ITenantService
{
    Task<List<TenantDTO>> GetTenantsAsync(CancellationToken cancellationToken = default);
    Task<TenantDTO> GetTenantByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<TenantDTO> CreateTenantAsync(TenantCreateDTO dto, CancellationToken cancellationToken = default);
    Task<TenantDTO> UpdateTenantAsync(int id, TenantUpdateDTO dto, CancellationToken cancellationToken = default);
    Task<TenantDTO> DeactivateTenantAsync(int id, string? modifiedBy, CancellationToken cancellationToken = default);
}
