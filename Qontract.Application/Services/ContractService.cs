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

    public async Task<List<ContractDTO>> GetContractsAsync(CancellationToken cancellationToken = default)
    {
        var contracts = await _context.Contracts
            .Include(c => c.Tenant)
            .Include(c => c.Contractor)
            .Include(c => c.Creator)
            .Include(c => c.Modifier)
            .Where(c => c.IsActive)
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => new ContractDTO
            {
                Id = c.Id,
                Title = c.Title,
                Description = c.Description,
                Status = c.Status,
                StartDate = c.StartDate,
                EndDate = c.EndDate,
                Counterparty = c.Counterparty,
                Currency = c.Currency,
                Value = c.Value,
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

    public async Task<ContractDTO> GetContractByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var contract = await _context.Contracts
            .Include(c => c.Tenant)
            .Include(c => c.Contractor)
            .Include(c => c.Creator)
            .Include(c => c.Modifier)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

        if (contract == null)
            throw new KeyNotFoundException($"Contract with ID {id} not found.");

        return new ContractDTO
        {
            Id = contract.Id,
            Title = contract.Title,
            Description = contract.Description,
            Status = contract.Status,
            StartDate = contract.StartDate,
            EndDate = contract.EndDate,
            Counterparty = contract.Counterparty,
            Currency = contract.Currency,
            Value = contract.Value,
            ContractorId = contract.ContractorId,
            CreatedAt = contract.CreatedAt,
            UpdatedAt = contract.UpdatedAt,
            CreatedBy = contract.CreatedBy,
            ModifiedBy = contract.ModifiedBy,
            CreatedAtUTC = contract.CreatedAt
        };
    }

    public async Task<ContractDTO> CreateContractAsync(ContractCreateDTO dto, string? createdBy, CancellationToken cancellationToken = default)
    {
        var contract = new Contract
        {
            Title = dto.Title,
            Description = dto.Description,
            Status = dto.Status,
            StartDate = dto.StartDate,
            EndDate = dto.EndDate,
            Counterparty = dto.Counterparty,
            Currency = dto.Currency,
            Value = dto.Value,
            ContractorId = dto.ContractorId,
            IsActive = true,
            CreatedBy = int.Parse(createdBy),
            ModifiedBy = int.Parse(createdBy),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _context.Contracts.Add(contract);
        await _context.SaveChangesAsync(cancellationToken);

        return await GetContractByIdAsync(contract.Id, cancellationToken);
    }

    public async Task<ContractDTO> UpdateContractAsync(int id, ContractUpdateDTO dto, string? modifiedBy, CancellationToken cancellationToken = default)
    {
        var contract = await _context.Contracts
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

        if (contract == null)
            throw new KeyNotFoundException($"Contract with ID {id} not found.");

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

        if (!string.IsNullOrEmpty(dto.Counterparty))
            contract.Counterparty = dto.Counterparty;

        if (!string.IsNullOrEmpty(dto.Currency))
            contract.Currency = dto.Currency;

        if (!string.IsNullOrEmpty(dto.Value))
            contract.Value = dto.Value;

        if (dto.ContractorId.HasValue)
            contract.ContractorId = dto.ContractorId.Value;

        contract.UpdatedAt = DateTime.UtcNow;
        contract.ModifiedBy = int.Parse(modifiedBy);

        await _context.SaveChangesAsync(cancellationToken);

        return await GetContractByIdAsync(id, cancellationToken);
    }

    public async Task<ContractDTO> DeactivateContractAsync(int id, string? modifiedBy, CancellationToken cancellationToken = default)
    {
        var contract = await _context.Contracts
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

        if (contract == null)
            throw new KeyNotFoundException($"Contract with ID {id} not found.");

        contract.IsActive = false;
        contract.UpdatedAt = DateTime.UtcNow;
        contract.ModifiedBy = int.Parse(modifiedBy);

        await _context.SaveChangesAsync(cancellationToken);

        return await GetContractByIdAsync(id, cancellationToken);
    }

    public async Task<bool> DeleteContractSoftAsync(int id, string? modifiedBy, CancellationToken cancellationToken = default)
    {
        var contract = await _context.Contracts
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

        if (contract == null)
            throw new KeyNotFoundException($"Contract with ID {id} not found.");

        contract.IsActive = false;
        contract.UpdatedAt = DateTime.UtcNow;
        contract.ModifiedBy = int.Parse(modifiedBy);

        await _context.SaveChangesAsync(cancellationToken);

        return true;
    }
}
