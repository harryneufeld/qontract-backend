using Qontract.Domain.Models;
using Qontract.Application.Models;
using Qontract.Application.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace Qontract.Application.Services;

public class AddressService : IAddressService
{
    private readonly IApplicationDbContext _context;

    public AddressService(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<AddressDTO> CreateAddressAsync(AddressCreateDTO dto, int entityId, EntityType entityType, string? createdBy, CancellationToken cancellationToken = default)
    {
        var address = new Address
        {
            EntityType = dto.EntityType,
            Street = dto.Street,
            City = dto.City,
            Zip = dto.Zip,
            Country = dto.Country,
            CreatedAt = DateTime.UtcNow,
            ModifiedAt = DateTime.UtcNow
        };

        switch (entityType)
        {
            case EntityType.Tenant:
                address.TenantId = entityId;
                break;
            case EntityType.Contractor:
                address.ContractorId = entityId;
                break;
        }

        _context.Addresses.Add(address);
        await _context.SaveChangesAsync(cancellationToken);

        return await GetAddressByIdAsync(address.Id, cancellationToken);
    }

    public async Task<AddressDTO> UpdateAddressAsync(int id, AddressCreateDTO dto, string? modifiedBy, CancellationToken cancellationToken = default)
    {
        var address = await _context.Addresses
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

        if (address == null)
            throw new KeyNotFoundException($"Address with ID {id} not found.");

        if (!string.IsNullOrEmpty(dto.Street))
            address.Street = dto.Street;

        if (!string.IsNullOrEmpty(dto.City))
            address.City = dto.City;

        if (!string.IsNullOrEmpty(dto.Zip))
            address.Zip = dto.Zip;

        if (!string.IsNullOrEmpty(dto.Country))
            address.Country = dto.Country;

        address.ModifiedAt = DateTime.UtcNow;
        address.ModifiedBy = modifiedBy ?? string.Empty;

        await _context.SaveChangesAsync(cancellationToken);

        return await GetAddressByIdAsync(id, cancellationToken);
    }

    public async Task<AddressDTO> GetAddressByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var address = await _context.Addresses
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

        if (address == null)
            throw new KeyNotFoundException($"Address with ID {id} not found.");

        return new AddressDTO
        {
            Id = address.Id,
            EntityType = address.EntityType,
            Street = address.Street,
            City = address.City,
            Zip = address.Zip,
            Country = address.Country,
            CreatedAt = address.CreatedAt,
            ModifiedAt = address.ModifiedAt
        };
    }

    /// <summary>
    /// Soft-deactivates the address: <c>Address</c> has no <c>IsActive</c>
    /// flag (see the tenant-isolation issue), so deactivation blanks the
    /// fields instead of deleting the row. Returns the deactivated DTO —
    /// previously the method was misnamed <c>DeleteAddressAsync</c> and
    /// returned a bare bool while the endpoint promised an AddressDTO.
    /// </summary>
    public async Task<AddressDTO> DeactivateAddressAsync(int id, string? modifiedBy, CancellationToken cancellationToken = default)
    {
        var address = await _context.Addresses
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

        if (address == null)
            throw new KeyNotFoundException($"Address with ID {id} not found.");

        address.Street = string.Empty;
        address.City = string.Empty;
        address.Zip = string.Empty;
        address.Country = string.Empty;
        address.ModifiedAt = DateTime.UtcNow;
        address.ModifiedBy = modifiedBy ?? string.Empty;

        await _context.SaveChangesAsync(cancellationToken);

        return await GetAddressByIdAsync(id, cancellationToken);
    }
}
