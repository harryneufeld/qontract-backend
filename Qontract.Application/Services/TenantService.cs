using Qontract.Domain.Models;
using Qontract.Application.Models;
using Qontract.Application.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace Qontract.Application.Services;

public class TenantService : ITenantService
{
    private readonly IApplicationDbContext _context;
    private readonly ILogger<TenantService> _logger;

    public TenantService(IApplicationDbContext context, ILogger<TenantService> logger)
    {
        _context = context;
        _logger = logger;
    }

    private static void ValidateContractRetention(string? settings)
    {
        if (string.IsNullOrWhiteSpace(settings)) return;
        try
        {
            using var json = System.Text.Json.JsonDocument.Parse(settings);
            if (json.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object
                && json.RootElement.TryGetProperty("contractRetentionDays", out var value)
                && (value.ValueKind != System.Text.Json.JsonValueKind.Number || !value.TryGetInt32(out var days) || days < 1 || days > 3650))
                throw new InvalidOperationException("Contract retention must be between 1 and 3650 days.");
        }
        catch (System.Text.Json.JsonException) { throw new InvalidOperationException("Tenant settings must be valid JSON."); }
    }

    private IQueryable<Tenant> AdministeredTenants(int userId) =>
        _context.Tenants.Where(t => _context.TenantMemberships.Any(m =>
            m.TenantId == t.Id && m.UserId == userId && m.IsActive
            && m.Role == UserRole.TenantAdmin && m.User.IsActive));

    public async Task<List<TenantDTO>> GetTenantsAsync(int userId, CancellationToken cancellationToken = default)
    {
        try
        {
            var tenants = await AdministeredTenants(userId)
                .Where(t => t.IsActive)
                .Select(t => new TenantDTO
                {
                    Id = t.Id,
                    Name = t.Name,
                    Type = t.Type,
                    Settings = t.Settings,
                    IsActive = t.IsActive,
                    CreatedAt = t.CreatedAt,
                    CreatedBy = t.CreatedBy,
                    ModifiedBy = t.ModifiedBy,
                    ModifiedAt = t.ModifiedAt
                })
                .ToListAsync(cancellationToken);

            _logger.LogInformation("Retrieved {Count} tenants", tenants.Count);
            return tenants;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving tenants");
            throw;
        }
    }

    public async Task<TenantDTO> GetTenantByIdAsync(int id, int userId, CancellationToken cancellationToken = default)
    {
        try
        {
            var tenant = await AdministeredTenants(userId)
                .FirstOrDefaultAsync(t => t.Id == id && t.IsActive, cancellationToken);

            if (tenant == null)
            {
                _logger.LogWarning("Tenant with ID {Id} not found", id);
                throw new KeyNotFoundException($"Tenant with ID {id} not found.");
            }

            return new TenantDTO
            {
                Id = tenant.Id,
                Name = tenant.Name,
                Type = tenant.Type,
                Settings = tenant.Settings,
                IsActive = tenant.IsActive,
                CreatedAt = tenant.CreatedAt,
                CreatedBy = tenant.CreatedBy,
                ModifiedBy = tenant.ModifiedBy,
                ModifiedAt = tenant.ModifiedAt
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving tenant with ID {Id}", id);
            throw;
        }
    }

    public async Task<TenantDTO> CreateTenantAsync(TenantCreateDTO dto, int userId, int currentTenantId, string? createdBy, CancellationToken cancellationToken = default)
    {
        ValidateContractRetention(dto.Settings);
        try
        {
            if (!await AdministeredTenants(userId).AnyAsync(t => t.Id == currentTenantId && t.IsActive, cancellationToken))
                throw new UnauthorizedAccessException();

            // DTO validation lives in the controller layer (issue #39).
            var tenant = new Tenant
            {
                Name = dto.Name,
                Type = dto.Type,
                Settings = dto.Settings,
                IsActive = dto.IsActive,
                CreatedBy = createdBy,
                ModifiedBy = createdBy,
                CreatedAt = DateTime.UtcNow,
                ModifiedAt = DateTime.UtcNow
            };

            _context.Tenants.Add(tenant);
            _context.TenantMemberships.Add(new TenantMembership
            {
                UserId = userId,
                Tenant = tenant,
                Role = UserRole.TenantAdmin,
                IsActive = true,
                CreatedBy = createdBy,
                ModifiedBy = createdBy
            });
            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Created tenant: {Name}", tenant.Name);
            return ToDto(tenant);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating tenant");
            throw;
        }
    }

    public async Task<TenantDTO> UpdateTenantAsync(int id, int userId, TenantUpdateDTO dto, string? modifiedBy, CancellationToken cancellationToken = default)
    {
        ValidateContractRetention(dto.Settings);
        try
        {
            var tenant = await AdministeredTenants(userId)
                .FirstOrDefaultAsync(t => t.Id == id && t.IsActive, cancellationToken);

            if (tenant == null)
            {
                _logger.LogWarning("Tenant with ID {Id} not found", id);
                throw new KeyNotFoundException($"Tenant with ID {id} not found.");
            }

            // DTO validation lives in the controller layer (issue #39).
            if (!string.IsNullOrEmpty(dto.Name))
            {
                tenant.Name = dto.Name;
            }

            if (dto.Type.HasValue)
            {
                tenant.Type = dto.Type.Value;
            }

            if (!string.IsNullOrEmpty(dto.Settings))
            {
                tenant.Settings = dto.Settings;
            }

            if (dto.IsActive.HasValue)
            {
                tenant.IsActive = dto.IsActive.Value;
            }

            tenant.ModifiedAt = DateTime.UtcNow;
            tenant.ModifiedBy = modifiedBy;

            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Updated tenant: {Name}", tenant.Name);
            // A successful update may deactivate the tenant. Return its
            // actual result rather than re-reading through the active filter.
            return ToDto(tenant);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating tenant with ID {Id}", id);
            throw;
        }
    }

    public async Task<TenantDTO> DeactivateTenantAsync(int id, int userId, string? modifiedBy, CancellationToken cancellationToken = default)
    {
        try
        {
            var tenant = await AdministeredTenants(userId)
                .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

            if (tenant == null)
            {
                _logger.LogWarning("Tenant with ID {Id} not found", id);
                throw new KeyNotFoundException($"Tenant with ID {id} not found.");
            }

            tenant.IsActive = false;
            tenant.ModifiedAt = DateTime.UtcNow;
            tenant.ModifiedBy = modifiedBy;

            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Deactivated tenant: {Name}", tenant.Name);

            // The row is now inactive; build the DTO from the tracked
            // entity instead of going through GetTenantByIdAsync (which
            // filters inactive tenants and would 404 on the row we just
            // deactivated) — same workaround as DeactivateContractAsync.
            return new TenantDTO
            {
                Id = tenant.Id,
                Name = tenant.Name,
                Type = tenant.Type,
                Settings = tenant.Settings,
                IsActive = tenant.IsActive,
                CreatedAt = tenant.CreatedAt,
                CreatedBy = tenant.CreatedBy,
                ModifiedBy = tenant.ModifiedBy,
                ModifiedAt = tenant.ModifiedAt
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deactivating tenant with ID {Id}", id);
            throw;
        }
    }

    private static TenantDTO ToDto(Tenant tenant) => new()
    {
        Id = tenant.Id,
        Name = tenant.Name,
        Type = tenant.Type,
        Settings = tenant.Settings,
        IsActive = tenant.IsActive,
        CreatedAt = tenant.CreatedAt,
        CreatedBy = tenant.CreatedBy,
        ModifiedAt = tenant.ModifiedAt,
        ModifiedBy = tenant.ModifiedBy
    };
}
