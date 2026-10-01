using Qontract.Domain.Models;
using Qontract.Application.Models;
using Qontract.Application.Interfaces;
using Microsoft.Extensions.Logging;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;

namespace Qontract.Application.Services;

public class UserService : IUserService
{
    private readonly IApplicationDbContext _context;
    private readonly IAuthService _authService;
    private readonly ILogger<UserService> _logger;

    public UserService(IApplicationDbContext context, IAuthService authService, ILogger<UserService> logger)
    {
        _context = context;
        _authService = authService;
        _logger = logger;
    }

    public async Task<List<UserDTO>> GetUsersAsync(int tenantId, CancellationToken cancellationToken = default)
    {
        try
        {
            var users = await _context.Users
                .Where(u => u.TenantId == tenantId)
                .Select(u => new UserDTO
                {
                    Id = u.Id,
                    Name = u.Name,
                    Email = u.Email,
                    Role = u.Role,
                    IsActive = u.IsActive,
                    TenantId = u.TenantId,
                    CreatedAt = u.CreatedAt,
                    ModifiedAt = u.ModifiedAt,
                    CreatedBy = u.CreatedBy,
                    ModifiedBy = u.ModifiedBy
                })
                .ToListAsync(cancellationToken);

            _logger.LogInformation("Retrieved {Count} users for tenant {TenantId}", users.Count, tenantId);
            return users;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving users for tenant {TenantId}", tenantId);
            throw;
        }
    }

    public async Task<UserDTO> GetUserByIdAsync(int id, int tenantId, CancellationToken cancellationToken = default)
    {
        try
        {
            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Id == id && u.TenantId == tenantId, cancellationToken);

            if (user == null)
            {
                _logger.LogWarning("User with ID {Id} not found in tenant {TenantId}", id, tenantId);
                throw new KeyNotFoundException($"User with ID {id} not found in tenant {tenantId}.");
            }

            return new UserDTO
            {
                Id = user.Id,
                Name = user.Name,
                Email = user.Email,
                Role = user.Role,
                IsActive = user.IsActive,
                TenantId = user.TenantId,
                CreatedAt = user.CreatedAt,
                ModifiedAt = user.ModifiedAt,
                CreatedBy = user.CreatedBy,
                ModifiedBy = user.ModifiedBy
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving user with ID {Id} from tenant {TenantId}", id, tenantId);
            throw;
        }
    }

    public async Task<UserDTO> CreateUserAsync(UserCreateDTO dto, string? createdBy, CancellationToken cancellationToken = default)
    {
        try
        {
            // DTO validation lives in the controller layer (issue #39).

            // Hash the plain password the client sent
            var hashedPassword = await _authService.HashPasswordAsync(dto.Password);

            var user = new User
            {
                Name = dto.Name,
                Email = dto.Email,
                PasswordHash = hashedPassword,
                Role = dto.Role,
                IsActive = true,
                TenantId = dto.TenantId,
                CreatedBy = createdBy,
                ModifiedBy = createdBy,
                CreatedAt = DateTime.UtcNow,
                ModifiedAt = DateTime.UtcNow
            };

            _context.Users.Add(user);
            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Created user: {Name} ({Email})", user.Name, user.Email);
            return await GetUserByIdAsync(user.Id, dto.TenantId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating user");
            throw;
        }
    }

    public async Task<UserDTO> UpdateUserAsync(int id, int tenantId, UserUpdateDTO dto, string? modifiedBy, CancellationToken cancellationToken = default)
    {
        try
        {
            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Id == id && u.TenantId == tenantId, cancellationToken);

            if (user == null)
            {
                _logger.LogWarning("User with ID {Id} not found in tenant {TenantId}", id, tenantId);
                throw new KeyNotFoundException($"User with ID {id} not found in tenant {tenantId}.");
            }

            // DTO validation lives in the controller layer (issue #39).
            if (!string.IsNullOrEmpty(dto.Name))
            {
                user.Name = dto.Name;
            }

            if (!string.IsNullOrEmpty(dto.Email))
            {
                user.Email = dto.Email;
            }

            if (!string.IsNullOrEmpty(dto.Password))
            {
                var hashedPassword = await _authService.HashPasswordAsync(dto.Password);
                user.PasswordHash = hashedPassword;
            }

            if (dto.Role.HasValue)
            {
                user.Role = dto.Role.Value;
            }

            if (dto.IsActive.HasValue)
            {
                user.IsActive = dto.IsActive.Value;
            }

            // Legacy user administration still edits the primary membership.
            // Commit its role/active state with the account, leaving any
            // additional tenant memberships unchanged.
            var membership = await _context.TenantMemberships.FirstOrDefaultAsync(
                m => m.UserId == id && m.TenantId == tenantId, cancellationToken);
            if (membership is not null)
            {
                membership.Role = user.Role;
                membership.IsActive = user.IsActive;
                membership.ModifiedBy = modifiedBy;
            }

            user.ModifiedAt = DateTime.UtcNow;
            user.ModifiedBy = modifiedBy;

            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Updated user: {Name}", user.Name);
            return await GetUserByIdAsync(id, tenantId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating user with ID {Id} from tenant {TenantId}", id, tenantId);
            throw;
        }
    }

    public async Task<UserDTO> DeactivateUserAsync(int id, int tenantId, string? modifiedBy, CancellationToken cancellationToken = default)
    {
        try
        {
            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Id == id && u.TenantId == tenantId, cancellationToken);

            if (user == null)
            {
                _logger.LogWarning("User with ID {Id} not found in tenant {TenantId}", id, tenantId);
                // Uniform 404: cross-tenant users are not distinguishable
                // from unknown ids, and (issue #32) nothing is committed.
                throw new KeyNotFoundException($"User with ID {id} not found in tenant {tenantId}.");
            }

            user.IsActive = false;
            user.ModifiedAt = DateTime.UtcNow;
            user.ModifiedBy = modifiedBy;

            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Deactivated user: {Name}", user.Name);
            return await GetUserByIdAsync(id, tenantId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deactivating user with ID {Id}", id);
            throw;
        }
    }
}
