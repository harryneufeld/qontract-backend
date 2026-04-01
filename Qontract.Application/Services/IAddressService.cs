namespace Qontract.Application.Services;

public interface IAddressService
{
    Task<AddressDTO> CreateAddressAsync(AddressCreateDTO dto, int entityId, EntityType entityType, string? createdBy, CancellationToken cancellationToken = default);
    Task<AddressDTO> UpdateAddressAsync(int id, AddressCreateDTO dto, string? modifiedBy, CancellationToken cancellationToken = default);
    Task<AddressDTO> GetAddressByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<bool> DeleteAddressAsync(int id, string? modifiedBy, CancellationToken cancellationToken = default);
}
