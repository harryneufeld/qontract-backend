using Microsoft.EntityFrameworkCore;
using Qontract.Domain.Models;
using Qontract.Application.Models;

namespace Qontract.Application.Services;

public class ContractService : IContractService
{
    private readonly IApplicationDbContext _context;
    private readonly Qontract.Application.Interfaces.IDocumentIdentity? _identity;

    public ContractService(IApplicationDbContext context, Qontract.Application.Interfaces.IDocumentIdentity? identity = null)
    {
        _context = context;
        _identity = identity;
    }

    /// <summary>
    /// Lists accessible, non-deleted contracts alphabetically, including inactive records.
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
        CancellationToken cancellationToken = default, string scope = "tenant", bool trash = false)
    {
        if (status is not null &&
            status is not ("active" or "inactive" or "expiring" or "expired" or "minDate"))
        {
            throw new ArgumentException(
                $"Unknown contract status filter '{status}'.", nameof(status));
        }

        IQueryable<Contract> query = _context.Contracts.VisibleIn(tenantId, _identity?.UserId)
            .Include(c => c.Tenant)
            .Include(c => c.Contractor)
            .Include(c => c.Creator)
            .Include(c => c.Modifier)
            .Include(c => c.ResponsibleUser)
            .Where(c => trash ? c.DeletedAt != null : c.DeletedAt == null);
        query = InScope(query, scope);

        if (status is not null)
        {
            var now = DateTime.Now;
            var today = now.Date;
            var rows = await query
                .Select(c => new { c.Id, c.Status, c.IsActive, c.EndDate, c.MinimumContractDate })
                .ToListAsync(cancellationToken);

            var matching = rows
                .Where(r => status switch
                {
                    "active" => r.IsActive && r.Status == ContractStatus.Active,
                    "inactive" => !r.IsActive,
                    "expiring" => r.IsActive && IsExpiringSoon(r.EndDate, now) &&
                                  !IsDateExpired(r.EndDate, now),
                    "expired" => IsDateExpired(r.EndDate, now) ||
                                 r.Status == ContractStatus.Expired,
                    "minDate" => r.IsActive && r.Status == ContractStatus.Active &&
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
            // ID keeps alphabetical pagination stable for equal titles.
            query = query
                .OrderBy(c => c.Title.ToLower())
                .ThenBy(c => c.Id);
        }

        if (page > 0 && pageSize > 0)
        {
            query = query.Skip((page - 1) * pageSize).Take(pageSize);
        }

        var contracts = await query
            .Select(c => new ContractDTO
            {
                Id = c.Id,
                TenantId = c.TenantId, OwnerUserId = c.OwnerUserId,
                ResponsibleUserId = c.ResponsibleUserId, ResponsibleUserName = c.ResponsibleUser == null ? null : c.ResponsibleUser.Name,
                IsActive = c.IsActive, DeletedAt = c.DeletedAt, RetentionExpiresAt = c.RetentionExpiresAt,
                Title = c.Title,
                Description = c.Description,
                Iban = c.Iban,
                Notes = c.Notes,
                Type = c.Type,
                Status = c.Status,
                StartDate = c.StartDate,
                EndDate = c.EndDate,
                Currency = c.Currency,
                Value = c.Value,
                BillingPeriod = c.BillingPeriod,
                PriceHistory = c.PriceHistory.OrderBy(p => p.EffectiveFrom).ThenBy(p => p.CreatedAt).Select(p => new ContractPriceDTO
                {
                    Id = p.Id, Value = p.Value, Currency = p.Currency, BillingPeriod = p.BillingPeriod,
                    EffectiveFrom = p.EffectiveFrom, EffectiveUntil = p.EffectiveUntil, Notes = p.Notes,
                    CreatedAt = p.CreatedAt, CreatedBy = p.CreatedBy
                }).ToList(),
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
    public async Task<ContractStatsDTO> GetStatsAsync(int tenantId, CancellationToken cancellationToken = default, string scope = "mine")
    {
        var now = DateTime.Now;
        var today = now.Date;

        var rows = await InScope(_context.Contracts.VisibleIn(tenantId, _identity?.UserId), scope)
            .Where(c => c.DeletedAt == null)
            .Select(c => new
            {
                c.Status,
                c.IsActive,
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
            if (c.IsActive && c.Status == ContractStatus.Active) active++;
            if (dateExpired || c.Status == ContractStatus.Expired) expired++;
            if (c.IsActive && IsExpiringSoon(c.EndDate, now) && !dateExpired) expiring++;
            if (c.IsActive && c.Status == ContractStatus.Active &&
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
        var contract = await _context.Contracts.VisibleIn(tenantId, _identity?.UserId)
            .Include(c => c.PriceHistory)
            .Include(c => c.Tenant)
            .Include(c => c.Contractor)
            .Include(c => c.Creator)
            .Include(c => c.Modifier)
            .Include(c => c.ResponsibleUser)
            .FirstOrDefaultAsync(c => c.Id == id && c.DeletedAt == null, cancellationToken);

        if (contract == null)
            throw new KeyNotFoundException($"Contract with ID {id} not found in tenant {tenantId}.");

        return new ContractDTO
        {
            Id = contract.Id,
            TenantId = contract.TenantId, OwnerUserId = contract.OwnerUserId,
            ResponsibleUserId = contract.ResponsibleUserId, ResponsibleUserName = contract.ResponsibleUser?.Name,
            IsActive = contract.IsActive, DeletedAt = contract.DeletedAt, RetentionExpiresAt = contract.RetentionExpiresAt,
            Title = contract.Title,
            Description = contract.Description,
            Iban = contract.Iban,
            Notes = contract.Notes,
            Type = contract.Type,
            Status = contract.Status,
            StartDate = contract.StartDate,
            EndDate = contract.EndDate,
            Currency = contract.Currency,
            Value = contract.Value,
            BillingPeriod = contract.BillingPeriod,
            PriceHistory = contract.PriceHistory.OrderBy(p => p.EffectiveFrom).ThenBy(p => p.CreatedAt).Select(ToPriceDto).ToList(),
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
        await ValidateContractorAsync(dto.ContractorId, tenantId, cancellationToken);
        if (dto.ClearResponsibleUser && dto.ResponsibleUserId != null)
            throw new InvalidOperationException("Choose a responsible member or clear the assignment, not both.");
        var responsible = dto.ClearResponsibleUser ? null : dto.ResponsibleUserId ?? _identity?.UserId;
        await ValidateResponsibleAsync(responsible, tenantId, cancellationToken);
        var contract = new Contract
        {
            Title = dto.Title,
            Description = dto.Description,
            Iban = dto.Iban,
            Notes = dto.Notes,
            Type = dto.Type,
            Status = dto.Status,
            StartDate = dto.StartDate,
            EndDate = dto.EndDate,
            Currency = dto.Currency,
            Value = CanonicalValue(dto.Value, dto.BillingPeriod),
            BillingPeriod = dto.BillingPeriod,
            ContractNumber = dto.ContractNumber,
            MinimumContractDate = dto.MinimumContractDate,
            ContractorId = dto.ContractorId,
            TenantId = tenantId == 0 ? null : tenantId,
            OwnerUserId = RecordAccess.Owner(tenantId, _identity?.UserId),
            ResponsibleUserId = responsible,
            IsActive = true,
            CreatedBy = createdBy,
            ModifiedBy = createdBy,
            CreatedAt = DateTime.UtcNow,
            ModifiedAt = DateTime.UtcNow
        };

        _context.Contracts.Add(contract);
        contract.PriceHistory.Add(Snapshot(contract, InitialDate(contract), createdBy, null));
        await _context.SaveChangesAsync(cancellationToken);

        return await GetContractByIdAsync(contract.Id, tenantId, cancellationToken);
    }

    public async Task<ContractDTO> UpdateContractAsync(int id, int tenantId, ContractUpdateDTO dto, string? modifiedBy, CancellationToken cancellationToken = default)
    {
        await using var transaction = await _context.BeginSerializableTransactionAsync(cancellationToken);
        var contract = await _context.Contracts.VisibleIn(tenantId, _identity?.UserId).Include(c => c.PriceHistory)
            .FirstOrDefaultAsync(c => c.Id == id && c.DeletedAt == null, cancellationToken);

        if (contract == null)
            throw new KeyNotFoundException($"Contract with ID {id} not found in tenant {tenantId}.");

        await ValidateContractorAsync(dto.ContractorId ?? contract.ContractorId, tenantId, cancellationToken);
        if (dto.ClearResponsibleUser && dto.ResponsibleUserId != null)
            throw new InvalidOperationException("Choose a responsible member or clear the assignment, not both.");
        if (dto.ResponsibleUserId.HasValue || dto.ClearResponsibleUser)
        {
            await ValidateResponsibleAsync(dto.ClearResponsibleUser ? null : dto.ResponsibleUserId, tenantId, cancellationToken);
            contract.ResponsibleUserId = dto.ClearResponsibleUser ? null : dto.ResponsibleUserId;
        }
        if (dto.IsActive.HasValue) contract.IsActive = dto.IsActive.Value;
        var oldValue = contract.Value;
        var oldCurrency = contract.Currency;
        var oldPeriod = contract.BillingPeriod;
        if (contract.PriceHistory.Count == 0)
            contract.PriceHistory.Add(Snapshot(contract, InitialDate(contract), contract.CreatedBy, null));

        if (!string.IsNullOrEmpty(dto.Title))
            contract.Title = dto.Title;

        if (!string.IsNullOrEmpty(dto.Description))
            contract.Description = dto.Description;

        if (dto.Iban is not null) contract.Iban = dto.Iban;
        if (dto.Notes is not null) contract.Notes = dto.Notes;
        if (dto.ClearType) contract.Type = null;
        else if (dto.Type.HasValue) contract.Type = dto.Type;

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

        if (dto.BillingPeriod.HasValue) contract.BillingPeriod = dto.BillingPeriod;
        contract.Value = CanonicalValue(contract.Value, contract.BillingPeriod);
        if (contract.Value != oldValue || contract.Currency != oldCurrency || contract.BillingPeriod != oldPeriod
            || !string.IsNullOrWhiteSpace(dto.PriceNotes) || dto.PriceEffectiveFrom != null)
        {
            var open = contract.PriceHistory.Single(p => p.EffectiveUntil == null);
            var from = dto.PriceEffectiveFrom ?? DateTime.UtcNow.ToString("yyyy-MM-dd");
            if (dto.PriceEffectiveFrom is null && string.CompareOrdinal(from, open.EffectiveFrom) < 0)
                from = open.EffectiveFrom;
            if (!DateOnly.TryParseExact(from, "yyyy-MM-dd", out _) || string.CompareOrdinal(from, open.EffectiveFrom) < 0)
                throw new InvalidOperationException("Price effective date must be a valid date on or after the current price starts.");
            open.EffectiveUntil = from;
            open.ModifiedBy = modifiedBy;
            contract.PriceHistory.Add(Snapshot(contract, from, modifiedBy, dto.PriceNotes));
        }
        contract.ModifiedAt = DateTime.UtcNow;
        contract.ModifiedBy = modifiedBy;
        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetContractByIdAsync(id, tenantId, cancellationToken);
    }

    public async Task<ContractDTO> DeactivateContractAsync(int id, int tenantId, string? modifiedBy, CancellationToken cancellationToken = default)
    {
        var contract = await _context.Contracts.VisibleIn(tenantId, _identity?.UserId).Include(c => c.PriceHistory)
            .FirstOrDefaultAsync(c => c.Id == id && c.DeletedAt == null, cancellationToken);

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
            TenantId = contract.TenantId, OwnerUserId = contract.OwnerUserId,
            ResponsibleUserId = contract.ResponsibleUserId, ResponsibleUserName = contract.ResponsibleUser?.Name,
            IsActive = contract.IsActive, DeletedAt = contract.DeletedAt, RetentionExpiresAt = contract.RetentionExpiresAt,
            Title = contract.Title,
            Description = contract.Description,
            Iban = contract.Iban,
            Notes = contract.Notes,
            Type = contract.Type,
            Status = contract.Status,
            StartDate = contract.StartDate,
            EndDate = contract.EndDate,
            Currency = contract.Currency,
            Value = contract.Value,
            BillingPeriod = contract.BillingPeriod,
            PriceHistory = contract.PriceHistory.OrderBy(p => p.EffectiveFrom).ThenBy(p => p.CreatedAt).Select(ToPriceDto).ToList(),
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

    private IQueryable<Contract> InScope(IQueryable<Contract> query, string scope)
    {
        if (scope is not ("mine" or "tenant")) throw new ArgumentException("Unknown contract scope.");
        return scope == "mine" && _identity?.UserId is int userId
            ? query.Where(c => c.ResponsibleUserId == userId || c.TenantId == null && c.OwnerUserId == userId) : query;
    }

    public async Task<List<ResponsibleMemberDTO>> GetResponsibleMembersAsync(int tenantId, CancellationToken cancellationToken = default)
    {
        if (tenantId == 0)
            return await _context.Users.Where(u => u.Id == _identity!.UserId && u.IsActive)
                .Select(u => new ResponsibleMemberDTO(u.Id, u.Name)).ToListAsync(cancellationToken);
        return await _context.TenantMemberships.Where(m => m.TenantId == tenantId && m.IsActive && m.User.IsActive)
            .OrderBy(m => m.User.Name).Select(m => new ResponsibleMemberDTO(m.UserId, m.User.Name)).ToListAsync(cancellationToken);
    }

    private async Task ValidateResponsibleAsync(int? userId, int tenantId, CancellationToken ct)
    {
        if (userId == null) return;
        var valid = tenantId == 0 ? userId == _identity?.UserId
            : await _context.TenantMemberships.AnyAsync(m => m.TenantId == tenantId && m.UserId == userId && m.IsActive && m.User.IsActive, ct);
        if (!valid) throw new InvalidOperationException("Responsible user must be an active member of this workspace.");
    }

    public async Task<ContractDTO> RestoreContractAsync(int id, int tenantId, string? actor, CancellationToken cancellationToken = default)
    {
        await using var transaction = await _context.BeginSerializableTransactionAsync(cancellationToken);
        var contract = await _context.Contracts.VisibleIn(tenantId, _identity?.UserId)
            .SingleOrDefaultAsync(c => c.Id == id && c.DeletedAt != null, cancellationToken)
            ?? throw new KeyNotFoundException("Deleted contract not found.");
        if (contract.RetentionExpiresAt <= DateTime.UtcNow) throw new InvalidOperationException("The restoration period has expired.");
        contract.DeletedAt = null;
        contract.DeletedBy = null;
        contract.RetentionExpiresAt = null;
        contract.ModifiedBy = actor;
        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetContractByIdAsync(id, tenantId, cancellationToken);
    }

    private async Task ValidateContractorAsync(int? id, int tenantId, CancellationToken ct)
    {
        if (id.HasValue && !await _context.Contractors.VisibleIn(tenantId, _identity?.UserId).AnyAsync(c => c.Id == id, ct))
            throw new KeyNotFoundException("Contractor not found in this workspace.");
    }

    private static string InitialDate(Contract c) => DateOnly.TryParseExact(c.StartDate, "yyyy-MM-dd", out var date)
        ? date.ToString("yyyy-MM-dd") : c.CreatedAt.ToString("yyyy-MM-dd");

    private static ContractPrice Snapshot(Contract c, string from, string? actor, string? notes) => new()
    {
        Contract = c, Value = c.Value, Currency = c.Currency, BillingPeriod = c.BillingPeriod,
        EffectiveFrom = from, CreatedBy = actor, ModifiedBy = actor, Notes = notes
    };

    private static ContractPriceDTO ToPriceDto(ContractPrice p) => new()
    {
        Id = p.Id, Value = p.Value, Currency = p.Currency, BillingPeriod = p.BillingPeriod,
        EffectiveFrom = p.EffectiveFrom, EffectiveUntil = p.EffectiveUntil, Notes = p.Notes,
        CreatedAt = p.CreatedAt, CreatedBy = p.CreatedBy
    };

    private static string? CanonicalValue(string? value, BillingPeriod? period)
    {
        if (period is null || string.IsNullOrWhiteSpace(value)) return value;
        if (!decimal.TryParse(value.Trim().Replace(',', '.'),
            System.Globalization.NumberStyles.AllowLeadingSign | System.Globalization.NumberStyles.AllowDecimalPoint,
            System.Globalization.CultureInfo.InvariantCulture, out var amount) || amount < 0)
            throw new InvalidOperationException("Billed amount must be a non-negative number without currency symbols or grouping separators.");
        return amount.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    public async Task<bool> DeleteContractSoftAsync(int id, int tenantId, string? modifiedBy, CancellationToken cancellationToken = default)
    {
        var contract = await _context.Contracts.VisibleIn(tenantId, _identity?.UserId).Include(c => c.PriceHistory)
            .FirstOrDefaultAsync(c => c.Id == id && c.DeletedAt == null, cancellationToken);

        if (contract == null)
            throw new KeyNotFoundException($"Contract with ID {id} not found in tenant {tenantId}.");

        var settings = tenantId == 0 ? null : await _context.Tenants.Where(t => t.Id == tenantId).Select(t => t.Settings).SingleAsync(cancellationToken);
        var days = TenantSettingsParser.GetContractRetentionDays(settings) ?? 90;
        contract.DeletedAt = DateTime.UtcNow;
        contract.DeletedBy = modifiedBy;
        contract.RetentionExpiresAt = contract.DeletedAt.Value.AddDays(days);
        contract.ModifiedAt = DateTime.UtcNow;
        contract.ModifiedBy = modifiedBy;

        await _context.SaveChangesAsync(cancellationToken);

        return true;
    }
}
