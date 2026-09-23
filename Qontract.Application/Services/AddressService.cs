using Qontract.Domain.Models;
using Qontract.Application.Models;
using Qontract.Application.Interfaces;
using Microsoft.EntityFrameworkCore;
using System.Threading;
using System.Threading.Tasks;

namespace Qontract.Application.Services;

/// <summary>
/// All address reads and writes are scoped to the caller's tenant (issue #32):
/// addresses are resolved through the tenant of their owning entity, and
/// cross-tenant lookups 404 exactly like unknown ids (no existence leaks).
/// </summary>
public class AddressService : IAddressService
{
    private readonly IApplicationDbContext _context;

    public AddressService(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<AddressDTO> CreateAddressAsync(AddressCreateDTO dto, int entityId, EntityType entityType, int tenantId, string? createdBy, CancellationToken cancellationToken = default)
    {
        var address = new Address
        {
            EntityType = entityType,
            Street = dto.Street,
            City = dto.City,
            Zip = dto.Zip,
            Country = dto.Country,
            CreatedBy = createdBy,
            ModifiedBy = createdBy,
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
            case EntityType.Contract:
                address.ContractId = entityId;
                break;
            case EntityType.Person:
                // No backing entity owns Person addresses; nothing to link.
                break;
            default:
                throw new InvalidOperationException($"Unknown address entity type {entityType}.");
        }

        if (entityType != EntityType.Tenant && entityType != EntityType.Person)
        {
            // The owning entity must exist and belong to the caller's tenant
            // (Tenant addresses are the caller's own tenant by construction).
            var owner = entityType switch
            {
                EntityType.Contractor => (BaseEntity?)await _context.Contractors
                    .AsNoTracking()
                    .FirstOrDefaultAsync(c => c.Id == entityId && c.TenantId == tenantId, cancellationToken),
                EntityType.Contract => (BaseEntity?)await _context.Contracts
                    .AsNoTracking()
                    .FirstOrDefaultAsync(c => c.Id == entityId && c.TenantId == tenantId, cancellationToken),
                _ => null
            };
            if (owner == null)
            {
                throw new KeyNotFoundException($"Address owner with ID {entityId} not found in tenant {tenantId}.");
            }
        }

        _context.Addresses.Add(address);
        await _context.SaveChangesAsync(cancellationToken);

        return await GetAddressByIdAsync(address.Id, tenantId, cancellationToken);
    }

    public async Task<AddressDTO> UpdateAddressAsync(int id, int tenantId, AddressCreateDTO dto, string? modifiedBy, CancellationToken cancellationToken = default)
    {
        var address = await FindInTenantAsync(id, tenantId, cancellationToken);

        if (!string.IsNullOrEmpty(dto.Street))
            address.Street = dto.Street;

        if (!string.IsNullOrEmpty(dto.City))
            address.City = dto.City;

        if (!string.IsNullOrEmpty(dto.Zip))
            address.Zip = dto.Zip;

        if (!string.IsNullOrEmpty(dto.Country))
            address.Country = dto.Country;

        address.ModifiedAt = DateTime.UtcNow;
        address.ModifiedBy = modifiedBy;

        await _context.SaveChangesAsync(cancellationToken);

        return await GetAddressByIdAsync(id, tenantId, cancellationToken);
    }

    public async Task<AddressDTO> GetAddressByIdAsync(int id, int tenantId, CancellationToken cancellationToken = default)
    {
        var address = await FindInTenantAsync(id, tenantId, cancellationToken);
        return ToDto(address);
    }

    /// <summary>
    /// Soft-deactivates the address (issue #32): flips <c>IsActive</c> to
    /// false — the address keeps its fields and the row, but is hidden from
    /// all reads. Pre-#32 this misnamed "delete" blanked Street/City/Zip/
    /// Country instead, because Address had no IsActive flag.
    /// </summary>
    public async Task<AddressDTO> DeactivateAddressAsync(int id, int tenantId, string? modifiedBy, CancellationToken cancellationToken = default)
    {
        var address = await FindInTenantAsync(id, tenantId, cancellationToken);

        address.IsActive = false;
        address.ModifiedAt = DateTime.UtcNow;
        address.ModifiedBy = modifiedBy;

        await _context.SaveChangesAsync(cancellationToken);

        // The row is now inactive; build the DTO from the tracked entity
        // instead of going through FindInTenantAsync (which filters
        // IsActive and would 404 on the row we just deactivated).
        return ToDto(address);
    }

    private async Task<Address> FindInTenantAsync(int id, int tenantId, CancellationToken cancellationToken)
    {
        var address = await _context.Addresses
            .FirstOrDefaultAsync(a => a.Id == id && a.IsActive
                && (a.EntityType == EntityType.Tenant && a.TenantId == tenantId
                    || (a.EntityType == EntityType.Contractor && _context.Contractors.Any(c => c.Id == a.ContractorId && c.TenantId == tenantId))
                    || (a.EntityType == EntityType.Contract && _context.Contracts.Any(c => c.Id == a.ContractId && c.TenantId == tenantId))),
                cancellationToken);

        if (address == null)
        {
            // Uniform 404: cross-tenant addresses are not distinguishable
            // from unknown ids (same convention as DocumentService).
            throw new KeyNotFoundException($"Address with id {id} not found in tenant {tenantId}.");
        }

        return address;
    }

    private static AddressDTO ToDto(Address a) => new()
    {
        Id = a.Id,
        EntityType = a.EntityType,
        Street = a.Street,
        City = a.City,
        Zip = a.Zip,
        Country = a.Country,
        CreatedAt = a.CreatedAt,
        ModifiedAt = a.ModifiedAt,
        IsActive = a.IsActive
    };
}
