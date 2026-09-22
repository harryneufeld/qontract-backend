using Microsoft.EntityFrameworkCore;
using Qontract.Domain.Models;
using Qontract.Application.Models;

namespace Qontract.Application.Services;

public class ContractService : IContractService
{
    private readonly IApplicationDbContext _context;

    public ContractService(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<List<ContractDTO>> GetContractsAsync(int tenantId, CancellationToken cancellationToken = default)
    {
        var contracts = await _context.Contracts
            .Include(c => c.Tenant)
            .Include(c => c.Contractor)
            .Include(c => c.Creator)
            .Include(c => c.Modifier)
            .Where(c => c.IsActive && c.TenantId == tenantId)
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => new ContractDTO
            {
                Id = c.Id,
                Title = c.Title,
                Description = c.Description,
                Status = c.Status,
                StartDate = c.StartDate,
                EndDate = c.EndDate,
                Currency = c.Currency,
                Value = c.Value,
                ContractNumber = c.ContractNumber,
                MinimumContractDate = c.MinimumContractDate,
                ContractorId = c.ContractorId,
                CreatedAt = c.CreatedAt,
                UpdatedAt = c.UpdatedAt,
                CreatedBy = c.CreatedBy,
                ModifiedBy = c.ModifiedBy,
                CreatedAtUTC = c.CreatedAt
            })
            .ToListAsync(cancellationToken);

        return contracts;
    }

    public async Task<ContractDTO> GetContractByIdAsync(int id, int tenantId, CancellationToken cancellationToken = default)
    {
        var contract = await _context.Contracts
            .Include(c => c.Tenant)
            .Include(c => c.Contractor)
            .Include(c => c.Creator)
            .Include(c => c.Modifier)
            .FirstOrDefaultAsync(c => c.Id == id && c.IsActive && c.TenantId == tenantId, cancellationToken);

        if (contract == null)
            throw new KeyNotFoundException($"Contract with ID {id} not found in tenant {tenantId}.");

        return new ContractDTO
        {
            Id = contract.Id,
            Title = contract.Title,
            Description = contract.Description,
            Status = contract.Status,
            StartDate = contract.StartDate,
            EndDate = contract.EndDate,
            Currency = contract.Currency,
            Value = contract.Value,
            ContractNumber = contract.ContractNumber,
            MinimumContractDate = contract.MinimumContractDate,
            ContractorId = contract.ContractorId,
            CreatedAt = contract.CreatedAt,
            UpdatedAt = contract.UpdatedAt,
            CreatedBy = contract.CreatedBy,
            ModifiedBy = contract.ModifiedBy,
            CreatedAtUTC = contract.CreatedAt
        };
    }

    public async Task<ContractDTO> CreateContractAsync(int tenantId, ContractCreateDTO dto, string? createdBy, CancellationToken cancellationToken = default)
    {
        var contract = new Contract
        {
            Title = dto.Title,
            Description = dto.Description,
            Status = dto.Status,
            StartDate = dto.StartDate,
            EndDate = dto.EndDate,
            Currency = dto.Currency,
            Value = dto.Value,
            ContractNumber = dto.ContractNumber,
            MinimumContractDate = dto.MinimumContractDate,
            ContractorId = dto.ContractorId,
            TenantId = tenantId,
            IsActive = true,
            CreatedBy = createdBy,
            ModifiedBy = createdBy,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _context.Contracts.Add(contract);
        await _context.SaveChangesAsync(cancellationToken);

        return await GetContractByIdAsync(contract.Id, tenantId, cancellationToken);
    }

    public async Task<ContractDTO> UpdateContractAsync(int id, int tenantId, ContractUpdateDTO dto, string? modifiedBy, CancellationToken cancellationToken = default)
    {
        var contract = await _context.Contracts
            .FirstOrDefaultAsync(c => c.Id == id && c.IsActive && c.TenantId == tenantId, cancellationToken);

        if (contract == null)
            throw new KeyNotFoundException($"Contract with ID {id} not found in tenant {tenantId}.");

        if (!string.IsNullOrEmpty(dto.Title))
            contract.Title = dto.Title;

        if (!string.IsNullOrEmpty(dto.Description))
            contract.Description = dto.Description;

        if (dto.Status.HasValue)
            contract.Status = dto.Status.Value;

        if (!string.IsNullOrEmpty(dto.StartDate))
            contract.StartDate = dto.StartDate;

        if (!string.IsNullOrEmpty(dto.EndDate))
            contract.EndDate = dto.EndDate;

        if (!string.IsNullOrEmpty(dto.Currency))
            contract.Currency = dto.Currency;

        if (!string.IsNullOrEmpty(dto.Value))
            contract.Value = dto.Value;

        if (!string.IsNullOrEmpty(dto.ContractNumber))
            contract.ContractNumber = dto.ContractNumber;

        if (!string.IsNullOrEmpty(dto.MinimumContractDate))
            contract.MinimumContractDate = dto.MinimumContractDate;

        if (dto.ContractorId.HasValue)
            contract.ContractorId = dto.ContractorId.Value;

        contract.UpdatedAt = DateTime.UtcNow;
        contract.ModifiedBy = modifiedBy;

        await _context.SaveChangesAsync(cancellationToken);

        return await GetContractByIdAsync(id, tenantId, cancellationToken);
    }

    public async Task<ContractDTO> DeactivateContractAsync(int id, int tenantId, string? modifiedBy, CancellationToken cancellationToken = default)
    {
        var contract = await _context.Contracts
            .FirstOrDefaultAsync(c => c.Id == id && c.IsActive && c.TenantId == tenantId, cancellationToken);

        if (contract == null)
            throw new KeyNotFoundException($"Contract with ID {id} not found in tenant {tenantId}.");

        contract.IsActive = false;
        contract.UpdatedAt = DateTime.UtcNow;
        contract.ModifiedBy = modifiedBy;

        await _context.SaveChangesAsync(cancellationToken);

        // The row is now inactive; build the DTO from the tracked entity
        // instead of going through GetContractByIdAsync (which filters
        // inactive rows and would 404).
        return new ContractDTO
        {
            Id = contract.Id,
            Title = contract.Title,
            Description = contract.Description,
            Status = contract.Status,
            StartDate = contract.StartDate,
            EndDate = contract.EndDate,
            Currency = contract.Currency,
            Value = contract.Value,
            ContractNumber = contract.ContractNumber,
            MinimumContractDate = contract.MinimumContractDate,
            ContractorId = contract.ContractorId,
            CreatedAt = contract.CreatedAt,
            UpdatedAt = contract.UpdatedAt,
            CreatedBy = contract.CreatedBy,
            ModifiedBy = contract.ModifiedBy,
            CreatedAtUTC = contract.CreatedAt
        };
    }

    public async Task<bool> DeleteContractSoftAsync(int id, int tenantId, string? modifiedBy, CancellationToken cancellationToken = default)
    {
        var contract = await _context.Contracts
            .FirstOrDefaultAsync(c => c.Id == id && c.IsActive && c.TenantId == tenantId, cancellationToken);

        if (contract == null)
            throw new KeyNotFoundException($"Contract with ID {id} not found in tenant {tenantId}.");

        contract.IsActive = false;
        contract.UpdatedAt = DateTime.UtcNow;
        contract.ModifiedBy = modifiedBy;

        await _context.SaveChangesAsync(cancellationToken);

        return true;
    }
}
