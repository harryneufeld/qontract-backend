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

    /// <summary>
    /// Lists a tenant's active contracts, most recently created first.
    ///
    /// When [page] &gt; 0 and [pageSize] &gt; 0 the result is a single page
    /// of at most [pageSize] rows (issue #43); otherwise the full list is
    /// returned to preserve the historic behaviour of GET /api/contracts.
    ///
    /// When [status] is one of <c>active</c>, <c>expiring</c>,
    /// <c>expired</c> or <c>minDate</c> the list is restricted to that
    /// dashboard bucket (app#36) using the exact same date rules as
    /// <see cref="GetStatsAsync"/>, so the filtered rows always match the
    /// stat-chip counters. <c>minDate</c> is ordered by the minimum
    /// contract date (most urgent first) instead of creation date.
    /// </summary>
    public async Task<List<ContractDTO>> GetContractsAsync(
        int tenantId,
        int page = 0,
        int pageSize = 0,
        string? status = null,
        CancellationToken cancellationToken = default)
    {
        if (status is not null &&
            status is not ("active" or "expiring" or "expired" or "minDate"))
        {
            throw new ArgumentException(
                $"Unknown contract status filter '{status}'.", nameof(status));
        }

        IQueryable<Contract> query = _context.Contracts
            .Include(c => c.Tenant)
            .Include(c => c.Contractor)
            .Include(c => c.Creator)
            .Include(c => c.Modifier)
            .Where(c => c.IsActive && c.TenantId == tenantId);

        if (status is not null)
        {
            var now = DateTime.Now;
            var today = now.Date;
            var rows = await query
                .Select(c => new { c.Id, c.Status, c.EndDate, c.MinimumContractDate })
                .ToListAsync(cancellationToken);

            var matching = rows
                .Where(r => status switch
                {
                    "active" => r.Status == ContractStatus.Active,
                    "expiring" => IsExpiringSoon(r.EndDate, now) &&
                                  !IsDateExpired(r.EndDate, now),
                    "expired" => IsDateExpired(r.EndDate, now) ||
                                 r.Status == ContractStatus.Expired,
                    "minDate" => r.Status == ContractStatus.Active &&
                                 ReachesMinimumContractDate(r.MinimumContractDate, today),
                    _ => false
                })
                .Select(r => r.Id)
                .ToHashSet();

            query = query.Where(c => matching.Contains(c.Id));

            if (status == "minDate")
            {
                // Most urgent first, mirroring the app's client-side sort.
                query = query
                    .OrderBy(c => c.MinimumContractDate)
                    .ThenByDescending(c => c.Id);
            }
        }

        if (status is not "minDate")
        {
            // Secondary sort keeps paging stable when CreatedAt collides
            // (multiple rows created within the same millisecond).
            query = query
                .OrderByDescending(c => c.CreatedAt)
                .ThenByDescending(c => c.Id);
        }

        if (page > 0 && pageSize > 0)
        {
            query = query.Skip((page - 1) * pageSize).Take(pageSize);
        }

        var contracts = await query
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
                ModifiedAt = c.ModifiedAt,
                CreatedBy = c.CreatedBy,
                ModifiedBy = c.ModifiedBy,
                CreatedAtUTC = c.CreatedAt
            })
            .ToListAsync(cancellationToken);

        return contracts;
    }

    /// <summary>
    /// Dashboard counters over ALL of a tenant's contracts, not just the
    /// loaded page (app#33). The date rules mirror the app's client-side
    /// logic so server and client agree on the same buckets:
    ///  - expired: end date has passed (local midnight &lt;= now), or the
    ///    status is <see cref="ContractStatus.Expired"/>
    ///  - expiring: end date within the next 30 days, not yet expired
    ///    (no status filter, mirroring the app's getExpiringContracts)
    ///  - minDateApproaching: status Active and the minimum contract date
    ///    falls within the next 90 days (already passed counts)
    /// </summary>
    public async Task<ContractStatsDTO> GetStatsAsync(int tenantId, CancellationToken cancellationToken = default)
    {
        var now = DateTime.Now;
        var today = now.Date;

        var rows = await _context.Contracts
            .Where(c => c.IsActive && c.TenantId == tenantId)
            .Select(c => new
            {
                c.Status,
                c.EndDate,
                c.MinimumContractDate
            })
            .ToListAsync(cancellationToken);

        var active = 0;
        var expiring = 0;
        var expired = 0;
        var minDateApproaching = 0;

        foreach (var c in rows)
        {
            var dateExpired = IsDateExpired(c.EndDate, now);
            if (c.Status == ContractStatus.Active) active++;
            if (dateExpired || c.Status == ContractStatus.Expired) expired++;
            if (IsExpiringSoon(c.EndDate, now) && !dateExpired) expiring++;
            if (c.Status == ContractStatus.Active &&
                ReachesMinimumContractDate(c.MinimumContractDate, today))
            {
                minDateApproaching++;
            }
        }

        return new ContractStatsDTO
        {
            Total = rows.Count,
            Active = active,
            Expiring = expiring,
            Expired = expired,
            MinDateApproaching = minDateApproaching
        };
    }

    /// Parses a 'yyyy-MM-dd' (or any parseable) date string to a local
    /// midnight, mirroring the app's Contract._parseDate.
    private static DateTime? ParseDateOnly(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return DateTime.TryParse(value, out var d) ? d.Date : null;
    }

    // App: end.isBefore(DateTime.now()) with end at local midnight.
    private static bool IsDateExpired(string? endDate, DateTime now)
    {
        var end = ParseDateOnly(endDate);
        return end.HasValue && end.Value <= now.Date;
    }

    // App: diff = end(midnight) - now; 0 <= diff.inDays <= 30 (truncated).
    private static bool IsExpiringSoon(string? endDate, DateTime now)
    {
        var end = ParseDateOnly(endDate);
        if (end == null) return false;
        var days = (end.Value - now).Days; // truncates toward zero, like Dart's inDays
        return days >= 0 && days <= 30;
    }

    // App: (minDateDay - today).inDays <= 90, both at local midnight.
    private static bool ReachesMinimumContractDate(string? minimumContractDate, DateTime today)
    {
        var min = ParseDateOnly(minimumContractDate);
        if (min == null) return false;
        return (min.Value - today).Days <= 90;
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
            ModifiedAt = contract.ModifiedAt,
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
            ModifiedAt = DateTime.UtcNow
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

        contract.ModifiedAt = DateTime.UtcNow;
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
        contract.ModifiedAt = DateTime.UtcNow;
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
            ModifiedAt = contract.ModifiedAt,
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
        contract.ModifiedAt = DateTime.UtcNow;
        contract.ModifiedBy = modifiedBy;

        await _context.SaveChangesAsync(cancellationToken);

        return true;
    }
}
