using Microsoft.EntityFrameworkCore;
using Qontract.Domain.Models;

namespace Qontract.Application.Interfaces;

public interface IApplicationDbContext
{
    DbSet<Tenant> Tenants { get; }
    DbSet<User> Users { get; }
    DbSet<Contractor> Contractors { get; }
    DbSet<Contract> Contracts { get; }
    DbSet<Document> Documents { get; }
    DbSet<Address> Addresses { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
