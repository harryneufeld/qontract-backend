using Qontract.Domain.Models;
using Qontract.Application.Models;

namespace Qontract.Application.Services;

public class ContractorService : IContractorService
{
    private readonly IApplicationDbContext _context;

    public ContractorService(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<List<ContractorDTO>> GetContractorsAsync(int tenantId, CancellationToken cancellationToken = default)
    {
        var counts = await GetContractCountsAsync(tenantId, cancellationToken);

        var contractors = await _context.Contractors
            .Where(c => c.TenantId == tenantId)
            .Select(c => new ContractorDTO
            {
                Id = c.Id,
                Name = c.Name,
                Type = c.Type,
                Email = c.Email,
                Phone = c.Phone,
                TenantId = c.TenantId,
                UserId = c.UserId,
                IsActive = c.IsActive,
                CreatedAt = c.CreatedAt,
                ModifiedAt = c.ModifiedAt,
                CreatedBy = c.CreatedBy,
                ModifiedBy = c.ModifiedBy
            })
            .ToListAsync(cancellationToken);

        // app#33: total contracts per contractor (server-side; the app's
        // contract list is paginated and cannot be used to count).
        foreach (var c in contractors)
        {
            c.ContractCount = counts.TryGetValue(c.Id, out var n) ? n : 0;
        }

        return contractors;
    }

    /// Active contracts of the tenant grouped by contractor, as a lookup
    /// (contractor id -> count).
    private async Task<Dictionary<int, int>> GetContractCountsAsync(int tenantId, CancellationToken cancellationToken)
    {
        var counts = await _context.Contracts
            .Where(c => c.IsActive && c.TenantId == tenantId && c.ContractorId != null)
            .GroupBy(c => c.ContractorId!.Value)
            .Select(g => new { ContractorId = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        return counts.ToDictionary(x => x.ContractorId, x => x.Count);
    }

    public async Task<ContractorDTO> GetContractorByIdAsync(int id, int tenantId, CancellationToken cancellationToken = default)
    {
        var contractor = await _context.Contractors
            .FirstOrDefaultAsync(c => c.Id == id && c.TenantId == tenantId, cancellationToken);

        if (contractor == null)
            throw new KeyNotFoundException($"Contractor with ID {id} not found in tenant {tenantId}.");

        var counts = await GetContractCountsAsync(tenantId, cancellationToken);

        return new ContractorDTO
        {
            Id = contractor.Id,
            Name = contractor.Name,
            Type = contractor.Type,
            Email = contractor.Email,
            Phone = contractor.Phone,
            TenantId = contractor.TenantId,
            UserId = contractor.UserId,
            IsActive = contractor.IsActive,
            ContractCount = counts.TryGetValue(contractor.Id, out var n) ? n : 0,
            CreatedAt = contractor.CreatedAt,
            ModifiedAt = contractor.ModifiedAt,
            CreatedBy = contractor.CreatedBy,
            ModifiedBy = contractor.ModifiedBy
        };
    }

    public async Task<ContractorDTO> CreateContractorAsync(ContractorCreateDTO dto, string? createdBy, CancellationToken cancellationToken = default)
    {
        var contractor = new Contractor
        {
            Name = dto.Name,
            Type = dto.Type,
            Email = dto.Email,
            Phone = dto.Phone,
            TenantId = dto.TenantId,
            UserId = dto.UserId,
            IsActive = true,
            CreatedBy = createdBy,
            ModifiedBy = createdBy,
            CreatedAt = DateTime.UtcNow,
            ModifiedAt = DateTime.UtcNow
        };

        _context.Contractors.Add(contractor);
        await _context.SaveChangesAsync(cancellationToken);

        return await GetContractorByIdAsync(contractor.Id, dto.TenantId, cancellationToken);
    }

    public async Task<ContractorDTO> UpdateContractorAsync(int id, int tenantId, ContractorUpdateDTO dto, string? modifiedBy, CancellationToken cancellationToken = default)
    {
        var contractor = await _context.Contractors
            .FirstOrDefaultAsync(c => c.Id == id && c.TenantId == tenantId, cancellationToken);

        if (contractor == null)
            throw new KeyNotFoundException($"Contractor with ID {id} not found in tenant {tenantId}.");

        if (!string.IsNullOrEmpty(dto.Name))
            contractor.Name = dto.Name;

        if (dto.Type.HasValue)
            contractor.Type = dto.Type.Value;

        if (dto.Email is not null)
            contractor.Email = dto.Email;

        if (dto.Phone is not null)
            contractor.Phone = dto.Phone;

        if (dto.IsActive.HasValue)
            contractor.IsActive = dto.IsActive.Value;

        contractor.ModifiedAt = DateTime.UtcNow;
        contractor.ModifiedBy = modifiedBy;

        await _context.SaveChangesAsync(cancellationToken);

        return await GetContractorByIdAsync(id, tenantId, cancellationToken);
    }

    public async Task<ContractorDTO> DeactivateContractorAsync(int id, int tenantId, string? modifiedBy, CancellationToken cancellationToken = default)
    {
        var contractor = await _context.Contractors
            .FirstOrDefaultAsync(c => c.Id == id && c.TenantId == tenantId, cancellationToken);

        if (contractor == null)
            throw new KeyNotFoundException($"Contractor with ID {id} not found in tenant {tenantId}.");

        contractor.IsActive = false;
        contractor.ModifiedAt = DateTime.UtcNow;
        contractor.ModifiedBy = modifiedBy;

        await _context.SaveChangesAsync(cancellationToken);

        return await GetContractorByIdAsync(id, tenantId, cancellationToken);
    }

    public async Task<ContractorDTO> ActivateContractorAsync(int id, int tenantId, string? modifiedBy, CancellationToken cancellationToken = default)
    {
        var contractor = await _context.Contractors
            .FirstOrDefaultAsync(c => c.Id == id && c.TenantId == tenantId, cancellationToken);

        if (contractor == null)
            throw new KeyNotFoundException($"Contractor with ID {id} not found in tenant {tenantId}.");

        contractor.IsActive = true;
        contractor.ModifiedAt = DateTime.UtcNow;
        contractor.ModifiedBy = modifiedBy;

        await _context.SaveChangesAsync(cancellationToken);

        return await GetContractorByIdAsync(id, tenantId, cancellationToken);
    }
}
