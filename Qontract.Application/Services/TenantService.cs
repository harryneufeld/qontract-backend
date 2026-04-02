using FluentValidation;
using Qontract.Domain.Models;
using Qontract.Application.Models;
using Qontract.Application.FluentValidation;
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

    public async Task<List<TenantDTO>> GetTenantsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var tenants = await _context.Tenants
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
                    UpdatedAt = t.UpdatedAt
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

    public async Task<TenantDTO> GetTenantByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        try
        {
            var tenant = await _context.Tenants
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
                UpdatedAt = tenant.UpdatedAt
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving tenant with ID {Id}", id);
            throw;
        }
    }

    public async Task<TenantDTO> CreateTenantAsync(TenantCreateDTO dto, CancellationToken cancellationToken = default)
    {
        try
        {
            // Validate DTO
            var validator = new TenantCreateValidator();
            var validationResults = await validator.ValidateAsync(dto, cancellationToken);

            if (!validationResults.IsValid)
            {
                _logger.LogWarning("Validation failed for tenant: {Error}", string.Join(", ", validationResults.Errors.Select(e => e.ErrorMessage)));
                throw new ValidationException("Validation failed: " + string.Join(", ", validationResults.Errors.Select(e => e.ErrorMessage)));
            }

            var tenant = new Tenant
            {
                Name = dto.Name,
                Type = dto.Type,
                Settings = dto.Settings,
                IsActive = dto.IsActive,
                CreatedBy = null,
                ModifiedBy = null,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _context.Tenants.Add(tenant);
            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Created tenant: {Name}", tenant.Name);
            return await GetTenantByIdAsync(tenant.Id, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating tenant");
            throw;
        }
    }

    public async Task<TenantDTO> UpdateTenantAsync(int id, TenantUpdateDTO dto, CancellationToken cancellationToken = default)
    {
        try
        {
            var tenant = await _context.Tenants
                .FirstOrDefaultAsync(t => t.Id == id && t.IsActive, cancellationToken);

            if (tenant == null)
            {
                _logger.LogWarning("Tenant with ID {Id} not found", id);
                throw new KeyNotFoundException($"Tenant with ID {id} not found.");
            }

            // Validate DTO
            var validator = new TenantUpdateValidator();
            var validationResults = await validator.ValidateAsync(dto, cancellationToken);

            if (!validationResults.IsValid)
            {
                _logger.LogWarning("Validation failed for tenant update: {Error}", string.Join(", ", validationResults.Errors.Select(e => e.ErrorMessage)));
                throw new ValidationException("Validation failed: " + string.Join(", ", validationResults.Errors.Select(e => e.ErrorMessage)));
            }

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

            tenant.UpdatedAt = DateTime.UtcNow;
            tenant.ModifiedBy = null;

            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Updated tenant: {Name}", tenant.Name);
            return await GetTenantByIdAsync(id, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating tenant with ID {Id}", id);
            throw;
        }
    }

    public async Task<TenantDTO> DeactivateTenantAsync(int id, string? modifiedBy, CancellationToken cancellationToken = default)
    {
        try
        {
            var tenant = await _context.Tenants
                .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

            if (tenant == null)
            {
                _logger.LogWarning("Tenant with ID {Id} not found", id);
                throw new KeyNotFoundException($"Tenant with ID {id} not found.");
            }

            tenant.IsActive = false;
            tenant.UpdatedAt = DateTime.UtcNow;
            tenant.ModifiedBy = modifiedBy;

            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Deactivated tenant: {Name}", tenant.Name);
            return await GetTenantByIdAsync(id, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deactivating tenant with ID {Id}", id);
            throw;
        }
    }
}
