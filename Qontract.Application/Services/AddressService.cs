using Qontract.Domain.Models;
using Qontract.Application.Models;
using Qontract.Application.Interfaces;
using Microsoft.EntityFrameworkCore;
using System.Threading;
using System.Threading.Tasks;

namespace Qontract.Application.Services;

/// <summary>
/// Account addresses are private to their owner across workspaces. Tenant
/// addresses are member-readable and admin-writable; contract/contractor
/// addresses inherit their entity's scope. Foreign IDs return a uniform 404.
/// </summary>
public class AddressService : IAddressService
{
    private readonly IApplicationDbContext _context;
    private readonly Qontract.Application.Interfaces.IDocumentIdentity? _identity;

    public AddressService(IApplicationDbContext context, Qontract.Application.Interfaces.IDocumentIdentity? identity = null)
    {
        _context = context;
        _identity = identity;
    }

    public async Task<AddressDTO> CreateAddressAsync(AddressCreateDTO dto, int entityId, EntityType entityType, int tenantId, string? createdBy, CancellationToken cancellationToken = default)
    {
        if (entityType == EntityType.Tenant && (tenantId <= 0 || entityId != tenantId))
            throw new KeyNotFoundException("Tenant address owner not found.");
        if (entityType == EntityType.Tenant) await RequireTenantAdminAsync(tenantId, cancellationToken);
        if (entityType == EntityType.Person && (_identity?.UserId is not > 0 || entityId != _identity.UserId))
            throw new KeyNotFoundException("Account address owner not found.");
        var address = new Address
        {
            EntityType = entityType,
            Street = dto.Street,
            HouseNumber = dto.HouseNumber,
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
                address.UserId = _identity!.UserId;
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
                EntityType.Contractor => (BaseEntity?)await _context.Contractors.VisibleIn(tenantId, _identity?.UserId)
                    .AsNoTracking()
                    .FirstOrDefaultAsync(c => c.Id == entityId, cancellationToken),
                EntityType.Contract => (BaseEntity?)await _context.Contracts.VisibleIn(tenantId, _identity?.UserId)
                    .AsNoTracking()
                    .FirstOrDefaultAsync(c => c.Id == entityId, cancellationToken),
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
        if (address.EntityType == EntityType.Tenant) await RequireTenantAdminAsync(tenantId, cancellationToken);

        if (new[] { dto.Street ?? address.Street, dto.HouseNumber ?? address.HouseNumber,
                dto.City ?? address.City, dto.Zip ?? address.Zip, dto.Country ?? address.Country }
            .All(value => string.IsNullOrWhiteSpace(value)))
            throw new InvalidOperationException("Enter at least one address field.");

        if (dto.Street is not null)
            address.Street = dto.Street;

        if (dto.HouseNumber is not null) address.HouseNumber = dto.HouseNumber;

        if (dto.City is not null)
            address.City = dto.City;

        if (dto.Zip is not null)
            address.Zip = dto.Zip;

        if (dto.Country is not null)
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
        if (address.EntityType == EntityType.Tenant) await RequireTenantAdminAsync(tenantId, cancellationToken);

        address.IsActive = false;
        address.ModifiedAt = DateTime.UtcNow;
        address.ModifiedBy = modifiedBy;

        await _context.SaveChangesAsync(cancellationToken);

        // The row is now inactive; build the DTO from the tracked entity
        // instead of going through FindInTenantAsync (which filters
        // IsActive and would 404 on the row we just deactivated).
        return ToDto(address);
    }

    public async Task<List<AddressDTO>> GetAddressesForContractorAsync(int contractorId, int tenantId, CancellationToken cancellationToken = default)
    {
        // Uniform 404: cross-tenant contractors are not distinguishable
        // from unknown ids (same convention as the rest of this service).
        var contractorExists = await _context.Contractors.VisibleIn(tenantId, _identity?.UserId)
            .AsNoTracking()
            .AnyAsync(c => c.Id == contractorId, cancellationToken);

        if (!contractorExists)
        {
            throw new KeyNotFoundException($"Contractor with ID {contractorId} not found in tenant {tenantId}.");
        }

        return await VisibleAddresses(tenantId)
            .Where(a => a.IsActive
                && a.EntityType == EntityType.Contractor
                && a.ContractorId == contractorId)
            .OrderBy(a => a.Id)
            .Select(a => new AddressDTO
            {
                Id = a.Id,
                EntityType = a.EntityType,
                Street = a.Street,
                HouseNumber = a.HouseNumber,
                City = a.City,
                Zip = a.Zip,
                Country = a.Country,
                CreatedAt = a.CreatedAt,
                ModifiedAt = a.ModifiedAt,
                IsActive = a.IsActive
            })
            .ToListAsync(cancellationToken);
    }

    private async Task<Address> FindInTenantAsync(int id, int tenantId, CancellationToken cancellationToken)
    {
        var address = await VisibleAddresses(tenantId)
            .FirstOrDefaultAsync(a => a.Id == id && a.IsActive,
                cancellationToken);

        if (address == null)
        {
            // Uniform 404: cross-tenant addresses are not distinguishable
            // from unknown ids (same convention as DocumentService).
            throw new KeyNotFoundException($"Address with id {id} not found in tenant {tenantId}.");
        }

        return address;
    }

    private IQueryable<Address> VisibleAddresses(int tenantId)
    {
        var userId = _identity?.UserId;
        var contractors = _context.Contractors.VisibleIn(tenantId, userId);
        var contracts = _context.Contracts.VisibleIn(tenantId, userId);
        return _context.Addresses.Where(a =>
            a.EntityType == EntityType.Person && userId > 0 && a.UserId == userId
                && a.TenantId == null && a.ContractorId == null && a.ContractId == null
            || a.UserId == null && (
                tenantId > 0 && a.EntityType == EntityType.Tenant && a.TenantId == tenantId
                    && a.ContractorId == null && a.ContractId == null
                || a.EntityType == EntityType.Contractor && a.TenantId == null && a.ContractId == null
                    && contractors.Any(c => c.Id == a.ContractorId)
                || a.EntityType == EntityType.Contract && a.TenantId == null && a.ContractorId == null
                    && contracts.Any(c => c.Id == a.ContractId)));
    }

    public async Task<List<AddressDTO>> GetMyAddressesAsync(CancellationToken cancellationToken = default)
    {
        if (_identity?.UserId is not > 0) throw new UnauthorizedAccessException();
        var rows = await VisibleAddresses(0).AsNoTracking()
            .Where(a => a.IsActive && a.EntityType == EntityType.Person)
            .OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id).ToListAsync(cancellationToken);
        return rows.Select(ToDto).ToList();
    }

    public async Task<List<AddressDTO>> GetTenantAddressesAsync(int tenantId, CancellationToken cancellationToken = default)
    {
        if (tenantId <= 0) throw new KeyNotFoundException("Tenant not found.");
        var userId = _identity?.UserId;
        if (!await _context.TenantMemberships.AnyAsync(m => m.TenantId == tenantId && m.UserId == userId
            && m.IsActive && m.User.IsActive && m.Tenant.IsActive, cancellationToken))
            throw new KeyNotFoundException("Tenant not found.");
        var rows = await VisibleAddresses(tenantId).AsNoTracking()
            .Where(a => a.IsActive && a.EntityType == EntityType.Tenant)
            .OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id).ToListAsync(cancellationToken);
        return rows.Select(ToDto).ToList();
    }

    private async Task RequireTenantAdminAsync(int tenantId, CancellationToken cancellationToken)
    {
        var userId = _identity?.UserId;
        if (!await _context.TenantMemberships.AnyAsync(m => m.TenantId == tenantId && m.UserId == userId
            && m.IsActive && m.User.IsActive && m.Tenant.IsActive && m.Role == UserRole.TenantAdmin, cancellationToken))
            throw new UnauthorizedAccessException("Only tenant administrators can change tenant addresses.");
    }

    private static AddressDTO ToDto(Address a) => new()
    {
        Id = a.Id,
        EntityType = a.EntityType,
        Street = a.Street,
        HouseNumber = a.HouseNumber,
        City = a.City,
        Zip = a.Zip,
        Country = a.Country,
        CreatedAt = a.CreatedAt,
        ModifiedAt = a.ModifiedAt,
        IsActive = a.IsActive
    };
}
