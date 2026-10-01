using Microsoft.EntityFrameworkCore;
using Qontract.Domain.Models;

namespace Qontract.Application.Interfaces;

public interface IApplicationDbContext
{
    DbSet<Tenant> Tenants { get; }
    DbSet<User> Users { get; }
    DbSet<TenantMembership> TenantMemberships { get; }
    DbSet<TenantInvitation> TenantInvitations { get; }
    DbSet<Contractor> Contractors { get; }
    DbSet<Contract> Contracts { get; }
    DbSet<Document> Documents { get; }
    DbSet<DocumentLink> DocumentLinks { get; }
    DbSet<Invoice> Invoices { get; }
    DbSet<Address> Addresses { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    Task<Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction> BeginSerializableTransactionAsync(CancellationToken cancellationToken = default);
    Task<bool> TryRedeemInvitationAsync(Guid id, int userId, DateTime now, CancellationToken cancellationToken = default);
}
