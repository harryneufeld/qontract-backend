using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Qontract.Application.Models;
using Qontract.Domain.Models;

namespace Qontract.Application.Services;

public interface IAddressService
{
    Task<List<AddressDTO>> GetMyAddressesAsync(CancellationToken cancellationToken = default);
    Task<List<AddressDTO>> GetTenantAddressesAsync(int tenantId, CancellationToken cancellationToken = default);
    Task<AddressDTO> CreateAddressAsync(AddressCreateDTO dto, int entityId, EntityType entityType, int tenantId, string? createdBy, CancellationToken cancellationToken = default);
    Task<AddressDTO> UpdateAddressAsync(int id, int tenantId, AddressCreateDTO dto, string? modifiedBy, CancellationToken cancellationToken = default);
    Task<AddressDTO> GetAddressByIdAsync(int id, int tenantId, CancellationToken cancellationToken = default);
    Task<AddressDTO> DeactivateAddressAsync(int id, int tenantId, string? modifiedBy, CancellationToken cancellationToken = default);

    /// <summary>
    /// Active addresses of a contractor (issue #41). The contractor must
    /// belong to <paramref name="tenantId"/> or a <c>KeyNotFoundException</c>
    /// is thrown — cross-tenant ids 404 like unknown ones (no existence leaks).
    /// </summary>
    Task<List<AddressDTO>> GetAddressesForContractorAsync(int contractorId, int tenantId, CancellationToken cancellationToken = default);
}
