using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Qontract.Application.Interfaces;
using Qontract.Application.Models;
using Qontract.Domain.Models;

namespace Qontract.Application.Services;

public class UserService(IApplicationDbContext context, IAuthService authService, ILogger<UserService> logger) : IUserService
{
    private IQueryable<UserDTO> UsersInTenant(int tenantId) => context.TenantMemberships.Where(m => m.TenantId == tenantId)
        .Select(m => new UserDTO
        {
            Id = m.UserId, Name = m.User.Name, Email = m.User.Email, Role = m.Role,
            IsActive = m.IsActive && m.User.IsActive, TenantId = m.TenantId,
            CreatedAt = m.CreatedAt, ModifiedAt = m.ModifiedAt, CreatedBy = m.CreatedBy, ModifiedBy = m.ModifiedBy
        });

    public Task<List<UserDTO>> GetUsersAsync(int tenantId, CancellationToken cancellationToken = default) =>
        UsersInTenant(tenantId).ToListAsync(cancellationToken);

    public async Task<UserDTO> GetUserByIdAsync(int id, int tenantId, CancellationToken cancellationToken = default) =>
        await UsersInTenant(tenantId).SingleOrDefaultAsync(u => u.Id == id, cancellationToken)
        ?? throw new KeyNotFoundException("Member not found.");

    public async Task<UserDTO> CreateUserAsync(UserCreateDTO dto, string? createdBy, CancellationToken cancellationToken = default)
    {
        var normalized = dto.Email.Trim().ToUpperInvariant();
        if (await context.Users.AnyAsync(u => u.NormalizedEmail == normalized, cancellationToken))
            throw new InvalidOperationException("An account already exists. Use an invitation to add an existing user.");
        var user = new User
        {
            Name = dto.Name, Email = dto.Email.Trim(), PasswordHash = await authService.HashPasswordAsync(dto.Password),
            Role = dto.Role, IsActive = true, TenantId = dto.TenantId, CreatedBy = createdBy, ModifiedBy = createdBy
        };
        context.Users.Add(user);
        await context.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Created tenant member {UserId} in {TenantId}", user.Id, dto.TenantId);
        return await GetUserByIdAsync(user.Id, dto.TenantId, cancellationToken);
    }

    public async Task<UserDTO> UpdateUserAsync(int id, int tenantId, UserUpdateDTO dto, string? modifiedBy, CancellationToken cancellationToken = default)
    {
        await using var transaction = await context.BeginSerializableTransactionAsync(cancellationToken);
        var membership = await context.TenantMemberships.Include(m => m.User)
            .SingleOrDefaultAsync(m => m.UserId == id && m.TenantId == tenantId, cancellationToken)
            ?? throw new KeyNotFoundException("Member not found.");
        var user = membership.User;
        var nextRole = dto.Role ?? membership.Role;
        var nextActive = dto.IsActive ?? membership.IsActive;
        if (membership.IsActive && membership.Role == UserRole.TenantAdmin
            && (!nextActive || nextRole != UserRole.TenantAdmin)
            && !await context.TenantMemberships.AnyAsync(m => m.TenantId == tenantId && m.UserId != id
                && m.IsActive && m.User.IsActive && m.Role == UserRole.TenantAdmin, cancellationToken))
            throw new InvalidOperationException("The tenant must retain an active administrator.");

        var editsAccount = !string.IsNullOrEmpty(dto.Name) || !string.IsNullOrEmpty(dto.Email) || !string.IsNullOrEmpty(dto.Password);
        if (editsAccount && (user.TenantId != tenantId || await context.TenantMemberships.AnyAsync(
                m => m.UserId == id && m.TenantId != tenantId && m.IsActive, cancellationToken)))
            throw new InvalidOperationException("Shared account details must be changed by the account owner.");
        if (!string.IsNullOrEmpty(dto.Name)) user.Name = dto.Name;
        if (!string.IsNullOrEmpty(dto.Email))
        {
            var normalized = dto.Email.Trim().ToUpperInvariant();
            if (await context.Users.AnyAsync(u => u.Id != id && u.NormalizedEmail == normalized, cancellationToken))
                throw new InvalidOperationException("This email is already in use.");
            // An old-address reset link must stay invalid even if the email is
            // changed back later. Revoke outstanding sessions/links by version.
            if (user.NormalizedEmail != normalized) user.AuthVersion++;
            user.Email = dto.Email.Trim();
        }
        if (!string.IsNullOrEmpty(dto.Password))
        {
            user.PasswordHash = await authService.HashPasswordAsync(dto.Password);
            user.AuthVersion++;
        }
        membership.Role = nextRole;
        membership.IsActive = nextActive;
        membership.ModifiedBy = modifiedBy;
        // Retain the legacy primary-role projection for compatibility, but
        // account activity is independent of membership removal.
        if (user.TenantId == tenantId) user.Role = nextRole;
        if (editsAccount) user.ModifiedBy = modifiedBy;
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetUserByIdAsync(id, tenantId, cancellationToken);
    }

    public Task<UserDTO> DeactivateUserAsync(int id, int tenantId, string? modifiedBy, CancellationToken cancellationToken = default) =>
        UpdateUserAsync(id, tenantId, new UserUpdateDTO { IsActive = false }, modifiedBy, cancellationToken);
}
