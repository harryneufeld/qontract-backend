using Microsoft.EntityFrameworkCore;
using Qontract.Application.Interfaces;
using Qontract.Application.Models;
using Qontract.Domain.Models;

namespace Qontract.Application.Services;

public class UserService(IApplicationDbContext context) : IUserService
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

    public async Task<UserDTO> UpdateUserAsync(int id, int tenantId, UserUpdateDTO dto, string? modifiedBy, CancellationToken cancellationToken = default)
    {
        await using var transaction = await context.BeginSerializableTransactionAsync(cancellationToken);
        var membership = await context.TenantMemberships.Include(m => m.User)
            .SingleOrDefaultAsync(m => m.UserId == id && m.TenantId == tenantId, cancellationToken)
            ?? throw new KeyNotFoundException("Member not found.");
        var user = membership.User;
        if (dto.Role == UserRole.Contractor)
            throw new InvalidOperationException("Invalid role.");
        var nextRole = dto.Role ?? membership.Role;
        var nextActive = dto.IsActive ?? membership.IsActive;
        if (membership.IsActive && membership.Role == UserRole.TenantAdmin
            && (!nextActive || nextRole != UserRole.TenantAdmin)
            && !await context.TenantMemberships.AnyAsync(m => m.TenantId == tenantId && m.UserId != id
                && m.IsActive && m.User.IsActive && m.Role == UserRole.TenantAdmin, cancellationToken))
            throw new InvalidOperationException("The tenant must retain an active administrator.");

        membership.Role = nextRole;
        membership.IsActive = nextActive;
        membership.ModifiedBy = modifiedBy;
        // Retain the legacy primary-role projection for compatibility, but
        // account activity is independent of membership removal.
        if (user.TenantId == tenantId) user.Role = nextRole;
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetUserByIdAsync(id, tenantId, cancellationToken);
    }

    public Task<UserDTO> DeactivateUserAsync(int id, int tenantId, string? modifiedBy, CancellationToken cancellationToken = default) =>
        UpdateUserAsync(id, tenantId, new UserUpdateDTO { IsActive = false }, modifiedBy, cancellationToken);
}
