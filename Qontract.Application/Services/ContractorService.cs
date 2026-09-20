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
        var contractors = await _context.Contractors
            .Where(c => c.TenantId == tenantId)
            .Select(c => new ContractorDTO
            {
                Id = c.Id,
                Name = c.Name,
                Type = c.Type,
                ContactInfo = c.ContactInfo,
                TenantId = c.TenantId,
                UserId = c.UserId,
                IsActive = c.IsActive,
                CreatedAt = c.CreatedAt,
                UpdatedAt = c.UpdatedAt,
                CreatedBy = c.CreatedBy,
                ModifiedBy = c.ModifiedBy
            })
            .ToListAsync(cancellationToken);

        return contractors;
    }

    public async Task<ContractorDTO> GetContractorByIdAsync(int id, int tenantId, CancellationToken cancellationToken = default)
    {
        var contractor = await _context.Contractors
            .FirstOrDefaultAsync(c => c.Id == id && c.TenantId == tenantId, cancellationToken);

        if (contractor == null)
            throw new KeyNotFoundException($"Contractor with ID {id} not found in tenant {tenantId}.");

        return new ContractorDTO
        {
            Id = contractor.Id,
            Name = contractor.Name,
            Type = contractor.Type,
            ContactInfo = contractor.ContactInfo,
            TenantId = contractor.TenantId,
            UserId = contractor.UserId,
            IsActive = contractor.IsActive,
            CreatedAt = contractor.CreatedAt,
            UpdatedAt = contractor.UpdatedAt,
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
            ContactInfo = dto.ContactInfo,
            TenantId = dto.TenantId,
            UserId = dto.UserId,
            IsActive = true,
            CreatedBy = createdBy,
            ModifiedBy = createdBy,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
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

        if (!string.IsNullOrEmpty(dto.ContactInfo))
            contractor.ContactInfo = dto.ContactInfo;

        if (dto.IsActive.HasValue)
            contractor.IsActive = dto.IsActive.Value;

        contractor.UpdatedAt = DateTime.UtcNow;
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
        contractor.UpdatedAt = DateTime.UtcNow;
        contractor.ModifiedBy = modifiedBy;

        await _context.SaveChangesAsync(cancellationToken);

        return await GetContractorByIdAsync(id, tenantId, cancellationToken);
    }
}
