using Microsoft.Extensions.DependencyInjection;
using Qontract.Core.Security;
using Qontract.Domain.Models;
using Qontract.Infrastructure.Data;

namespace Qontract.Tests;

/// <summary>
/// Seeds member accounts directly. Administrators can no longer create
/// accounts with a chosen password over the API (issue #80); members join
/// through invitations, which is more ceremony than most tests need.
/// </summary>
internal static class TestAccounts
{
    public static async Task<(int Id, string Email)> CreateMemberAsync(IServiceProvider services, int tenantId,
        UserRole role = UserRole.User, string password = "password123", string? email = null)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        email ??= $"member-{Guid.NewGuid():N}@qontract.com";
        // The DbContext adds the matching TenantMembership for a primary TenantId.
        var user = new User
        {
            Name = "Seeded member", Email = email, PasswordHash = Passwords.Hash(password),
            Role = role, IsActive = true, TenantId = tenantId
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return (user.Id, email);
    }
}
