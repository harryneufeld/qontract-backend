using Microsoft.EntityFrameworkCore;
using Qontract.Domain.Models;
using Qontract.Application.Interfaces;

namespace Qontract.Infrastructure.Data;

public class ApplicationDbContext : DbContext, IApplicationDbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
    {
    }

    public DbSet<Tenant> Tenants { get; set; }
    public DbSet<User> Users { get; set; }
    public DbSet<TenantMembership> TenantMemberships { get; set; }
    public DbSet<TenantInvitation> TenantInvitations { get; set; }

    public Task<Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction> BeginSerializableTransactionAsync(CancellationToken cancellationToken = default) =>
        Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);

    public async Task<bool> TryRedeemInvitationAsync(Guid id, int userId, DateTime now, CancellationToken cancellationToken = default) =>
        await TenantInvitations.Where(i => i.Id == id && i.RevokedAt == null && i.RedeemedAt == null
                && i.ExpiresAt > now && i.Tenant.IsActive)
            .ExecuteUpdateAsync(setters => setters.SetProperty(i => i.RedeemedAt, now)
                .SetProperty(i => i.RedeemedByUserId, userId)
                .SetProperty(i => i.ModifiedAt, now), cancellationToken) == 1;
    public DbSet<Contractor> Contractors { get; set; }
    public DbSet<Contract> Contracts { get; set; }
    public DbSet<ContractPrice> ContractPrices { get; set; }
    public DbSet<Document> Documents { get; set; }
    public DbSet<Address> Addresses { get; set; }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        AddPrimaryMemberships();
        ApplyAuditTimestamps();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        AddPrimaryMemberships();
        ApplyAuditTimestamps();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>
    /// Maintains the audit timestamps on every saved entity (issue #16):
    /// new entities get their CreatedAt/ModifiedAt stamped on first save and
    /// ModifiedAt is refreshed on every update.
    /// </summary>
    // Keep existing seed/admin-create paths working while accounts still carry
    // their legacy primary TenantId. Both rows are committed atomically.
    private void AddPrimaryMemberships()
    {
        foreach (var entry in ChangeTracker.Entries<User>()
                     .Where(e => e.State is EntityState.Added or EntityState.Modified))
        {
            entry.Entity.Email = entry.Entity.Email.Trim();
            entry.Entity.NormalizedEmail = entry.Entity.Email.ToUpperInvariant();
        }
        foreach (var entry in ChangeTracker.Entries<User>()
                     .Where(e => e.State == EntityState.Added).ToList())
        {
            var user = entry.Entity;
            if (!TenantMemberships.Local.Any(m => m.User == user && m.TenantId == user.TenantId))
                TenantMemberships.Add(new TenantMembership
                {
                    User = user,
                    TenantId = user.TenantId,
                    Role = user.Role,
                    IsActive = user.IsActive,
                    CreatedBy = user.CreatedBy,
                    ModifiedBy = user.ModifiedBy
                });
        }
    }

    private void ApplyAuditTimestamps()
    {
        var now = DateTime.UtcNow;
        foreach (var entry in ChangeTracker.Entries<BaseEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAt = now;
                    entry.Entity.ModifiedAt = now;
                    break;
                case EntityState.Modified:
                    entry.Entity.ModifiedAt = now;
                    break;
            }
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Tenant
        modelBuilder.Entity<Tenant>(entity =>
        {
            entity.ToTable("Tenants");
            entity.HasKey(e => e.Id);
            // Client-side key generation (issue #26): portable across SQLite,
            // PostgreSQL and SQL Server — no provider-specific IDENTITY /
            // AUTOINCREMENT columns, so the shared migration chain works
            // everywhere. See ClientSideIdValueGenerator for the trade-off.
            entity.Property(e => e.Id)
                .ValueGeneratedOnAdd()
                .HasValueGenerator((p, t) => new ClientSideIdValueGenerator());
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Type).IsRequired();
            entity.Property(e => e.Settings).HasMaxLength(5000);
            entity.Property(e => e.CreatedBy).HasMaxLength(500);
            entity.Property(e => e.ModifiedBy).HasMaxLength(500);
        });

        // User
        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("Users");
            entity.HasKey(e => e.Id);
            // Client-side key generation (issue #26): portable across SQLite,
            // PostgreSQL and SQL Server — no provider-specific IDENTITY /
            // AUTOINCREMENT columns, so the shared migration chain works
            // everywhere. See ClientSideIdValueGenerator for the trade-off.
            entity.Property(e => e.Id)
                .ValueGeneratedOnAdd()
                .HasValueGenerator((p, t) => new ClientSideIdValueGenerator());
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Email).IsRequired().HasMaxLength(100);
            entity.Property(e => e.NormalizedEmail).IsRequired().HasMaxLength(100);
            entity.HasIndex(e => e.NormalizedEmail).IsUnique();
            entity.Property(e => e.PasswordHash).IsRequired().HasMaxLength(255);
            entity.Property(e => e.Role).IsRequired();
            entity.Property(e => e.TenantId).IsRequired();
            entity.Property(e => e.CreatedBy).HasMaxLength(500);
            entity.Property(e => e.ModifiedBy).HasMaxLength(500);
            entity.HasOne(e => e.Tenant)
                .WithMany()
                .HasForeignKey(e => e.TenantId);
        });

        modelBuilder.Entity<TenantMembership>(entity =>
        {
            entity.ToTable("TenantMemberships");
            entity.HasKey(e => new { e.UserId, e.TenantId });
            entity.Property(e => e.CreatedBy).HasMaxLength(500);
            entity.Property(e => e.ModifiedBy).HasMaxLength(500);
            entity.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Tenant).WithMany().HasForeignKey(e => e.TenantId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TenantInvitation>(entity =>
        {
            entity.ToTable("TenantInvitations");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.CodeHash).IsRequired().HasMaxLength(64);
            entity.HasIndex(e => e.CodeHash).IsUnique();
            entity.Property(e => e.Email).HasMaxLength(100);
            entity.Property(e => e.CreatedBy).HasMaxLength(500);
            entity.Property(e => e.ModifiedBy).HasMaxLength(500);
            entity.HasOne(e => e.Tenant).WithMany().HasForeignKey(e => e.TenantId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.RedeemedByUser).WithMany().HasForeignKey(e => e.RedeemedByUserId).OnDelete(DeleteBehavior.Restrict);
        });

        // Contractor
        modelBuilder.Entity<Contractor>(entity =>
        {
            entity.ToTable("Contractors");
            entity.HasKey(e => e.Id);
            // Client-side key generation (issue #26): portable across SQLite,
            // PostgreSQL and SQL Server — no provider-specific IDENTITY /
            // AUTOINCREMENT columns, so the shared migration chain works
            // everywhere. See ClientSideIdValueGenerator for the trade-off.
            entity.Property(e => e.Id)
                .ValueGeneratedOnAdd()
                .HasValueGenerator((p, t) => new ClientSideIdValueGenerator());
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Type).IsRequired();
            entity.Property(e => e.Email).HasMaxLength(200);
            entity.Property(e => e.Phone).HasMaxLength(50);
            entity.Property(e => e.CreatedBy).HasMaxLength(500);
            entity.Property(e => e.ModifiedBy).HasMaxLength(500);
            entity.HasOne(e => e.Tenant)
                .WithMany()
                .HasForeignKey(e => e.TenantId);
            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId);
        });

        // Contract
        modelBuilder.Entity<Contract>(entity =>
        {
            entity.ToTable("Contracts");
            entity.HasKey(e => e.Id);
            // Client-side key generation (issue #26): portable across SQLite,
            // PostgreSQL and SQL Server — no provider-specific IDENTITY /
            // AUTOINCREMENT columns, so the shared migration chain works
            // everywhere. See ClientSideIdValueGenerator for the trade-off.
            entity.Property(e => e.Id)
                .ValueGeneratedOnAdd()
                .HasValueGenerator((p, t) => new ClientSideIdValueGenerator());
            entity.Property(e => e.Title).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Description).HasMaxLength(2000);
            entity.Property(e => e.Status).IsRequired();
            entity.Property(e => e.StartDate).HasMaxLength(50);
            entity.Property(e => e.EndDate).HasMaxLength(50);
            entity.Property(e => e.Currency).HasMaxLength(50);
            entity.Property(e => e.Value).HasMaxLength(500);
            entity.Property(e => e.ContractNumber).HasMaxLength(100);
            entity.Property(e => e.MinimumContractDate).HasMaxLength(50);
            entity.Property(e => e.ContractorId);
            entity.Property(e => e.CreatedBy).HasMaxLength(500);
            entity.Property(e => e.ModifiedBy).HasMaxLength(500);
            entity.HasOne(e => e.Tenant)
                .WithMany()
                .HasForeignKey(e => e.TenantId);
            entity.HasOne(e => e.Contractor)
                .WithMany()
                .HasForeignKey(e => e.ContractorId);
            entity.HasMany(e => e.Documents)
                .WithOne(d => d.Contract)
                .HasForeignKey(d => d.ContractId);
        });

        modelBuilder.Entity<ContractPrice>(entity =>
        {
            entity.HasKey(p => p.Id);
            entity.Property(p => p.Id).ValueGeneratedNever();
            entity.Property(p => p.Value).HasMaxLength(500);
            entity.Property(p => p.Currency).HasMaxLength(50);
            entity.Property(p => p.EffectiveFrom).IsRequired().HasMaxLength(10);
            entity.Property(p => p.EffectiveUntil).HasMaxLength(10);
            entity.Property(p => p.Notes).HasMaxLength(2000);
            entity.Property(p => p.CreatedBy).HasMaxLength(500);
            entity.Property(p => p.ModifiedBy).HasMaxLength(500);
            entity.HasOne(p => p.Contract).WithMany(c => c.PriceHistory)
                .HasForeignKey(p => p.ContractId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(p => new { p.ContractId, p.EffectiveFrom });
        });

        // Document
        modelBuilder.Entity<Document>(entity =>
        {
            entity.ToTable("Documents");
            entity.HasKey(e => e.Id);
            // Client-side key generation (issue #26): portable across SQLite,
            // PostgreSQL and SQL Server — no provider-specific IDENTITY /
            // AUTOINCREMENT columns, so the shared migration chain works
            // everywhere. See ClientSideIdValueGenerator for the trade-off.
            entity.Property(e => e.Id)
                .ValueGeneratedOnAdd()
                .HasValueGenerator((p, t) => new ClientSideIdValueGenerator());
            entity.Property(e => e.FileName).IsRequired().HasMaxLength(255);
            entity.Property(e => e.FileType).IsRequired().HasMaxLength(200);
            entity.Property(e => e.FilePath).HasMaxLength(500);
            entity.Property(e => e.UploadedBy).HasMaxLength(500);
            entity.Property(e => e.CreatedBy).HasMaxLength(500);
            entity.Property(e => e.ModifiedBy).HasMaxLength(500);
            entity.Property(e => e.ContractId).IsRequired();
            entity.HasOne(e => e.Contract)
                .WithMany()
                .HasForeignKey(e => e.ContractId);
            // Self-referencing re-upload chain (issue #23): a superseded
            // version points at the version that replaced it. SetNull keeps
            // the retention purge from being blocked when a referenced row
            // is hard-deleted.
            entity.HasOne(e => e.SupersededBy)
                .WithMany()
                .HasForeignKey(e => e.SupersededById)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // Address
        modelBuilder.Entity<Address>(entity =>
        {
            entity.ToTable("Addresses");
            entity.HasKey(e => e.Id);
            // Client-side key generation (issue #26): portable across SQLite,
            // PostgreSQL and SQL Server — no provider-specific IDENTITY /
            // AUTOINCREMENT columns, so the shared migration chain works
            // everywhere. See ClientSideIdValueGenerator for the trade-off.
            entity.Property(e => e.Id)
                .ValueGeneratedOnAdd()
                .HasValueGenerator((p, t) => new ClientSideIdValueGenerator());
            entity.Property(e => e.EntityType).IsRequired();
            entity.Property(e => e.Street).HasMaxLength(500);
            entity.Property(e => e.City).HasMaxLength(100);
            entity.Property(e => e.Zip).HasMaxLength(20);
            entity.Property(e => e.Country).HasMaxLength(100);
            entity.Property(e => e.CreatedBy).HasMaxLength(500);
            entity.Property(e => e.ModifiedBy).HasMaxLength(500);
            entity.HasOne(e => e.Tenant)
                .WithMany()
                .HasForeignKey(e => e.TenantId);
            entity.HasOne(e => e.Contractor)
                .WithMany()
                .HasForeignKey(e => e.ContractorId);
            entity.HasOne(e => e.Contract)
                .WithMany()
                .HasForeignKey(e => e.ContractId);
        });
    }
}
