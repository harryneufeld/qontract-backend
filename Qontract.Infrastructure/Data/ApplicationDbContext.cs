using Microsoft.EntityFrameworkCore;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
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
    public DbSet<AuthSession> AuthSessions { get; set; }
    public DbSet<PasswordReset> PasswordResets { get; set; }
    public DbSet<EmailConfirmation> EmailConfirmations { get; set; }
    public DbSet<TermsAcceptance> TermsAcceptances { get; set; }
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
    public DbSet<DocumentLink> DocumentLinks { get; set; }
    public DbSet<Invoice> Invoices { get; set; }
    public DbSet<Address> Addresses { get; set; }
    public DbSet<Notification> Notifications { get; set; }

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
            if (!user.TenantId.HasValue) continue;
            if (!TenantMemberships.Local.Any(m => m.User == user && m.TenantId == user.TenantId))
                TenantMemberships.Add(new TenantMembership
                {
                    User = user,
                    TenantId = user.TenantId.Value,
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

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);
        if (Database.IsNpgsql())
        {
            configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeValueConverter>();
            configurationBuilder.Properties<DateTime?>().HaveConversion<UtcDateTimeValueConverter>();
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        // Keys come from ClientSideIdValueGenerator, so PostgreSQL needs no
        // IDENTITY columns (their sequences would never advance).
        if (Database.IsNpgsql())
            modelBuilder.HasAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.None);

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
            entity.Property(e => e.MutedNotificationKinds).HasMaxLength(500);
            entity.Property(e => e.TenantId).IsRequired(false);
            entity.Property(e => e.CreatedBy).HasMaxLength(500);
            entity.Property(e => e.ModifiedBy).HasMaxLength(500);
            entity.HasOne(e => e.Tenant)
                .WithMany()
                .HasForeignKey(e => e.TenantId);
        });

        modelBuilder.Entity<PasswordReset>(entity =>
        {
            entity.ToTable("PasswordResets");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.TokenHash).IsRequired().HasMaxLength(64);
            entity.Property(e => e.NormalizedEmail).IsRequired().HasMaxLength(100);
            entity.HasIndex(e => e.TokenHash).IsUnique();
            entity.HasIndex(e => new { e.UserId, e.CreatedAt });
            entity.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<EmailConfirmation>(entity =>
        {
            entity.ToTable("EmailConfirmations");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.TokenHash).IsRequired().HasMaxLength(64);
            entity.Property(e => e.NormalizedEmail).IsRequired().HasMaxLength(100);
            entity.Property(e => e.CodeHash).HasMaxLength(64);
            entity.HasIndex(e => e.TokenHash).IsUnique();
            entity.HasIndex(e => new { e.UserId, e.CreatedAt });
            entity.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Notification>(entity =>
        {
            entity.ToTable("Notifications");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.Kind).IsRequired().HasMaxLength(50);
            entity.Property(e => e.Params).IsRequired().HasMaxLength(2000);
            entity.Property(e => e.DedupKey).IsRequired().HasMaxLength(200);
            entity.Property(e => e.CreatedBy).HasMaxLength(500);
            entity.Property(e => e.ModifiedBy).HasMaxLength(500);
            entity.HasIndex(e => new { e.UserId, e.DedupKey }).IsUnique();
            entity.HasIndex(e => new { e.UserId, e.TenantId, e.ResolvedAt });
            entity.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Contract).WithMany().HasForeignKey(e => e.ContractId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TermsAcceptance>(entity =>
        {
            entity.ToTable("TermsAcceptances");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.Version).IsRequired().HasMaxLength(50);
            entity.HasIndex(e => new { e.UserId, e.Version }).IsUnique();
            entity.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<AuthSession>(entity =>
        {
            entity.ToTable("AuthSessions");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.HasIndex(e => new { e.UserId, e.ExpiresAt });
            entity.Property(e => e.RefreshTokenHash).HasMaxLength(64);
            entity.Property(e => e.PreviousRefreshTokenHash).HasMaxLength(64);
            entity.HasIndex(e => e.RefreshTokenHash).IsUnique();
            entity.HasIndex(e => e.PreviousRefreshTokenHash);
            entity.Property(e => e.ClientType).IsRequired().HasMaxLength(16).HasDefaultValue("api");
            entity.Property(e => e.DeviceName).HasMaxLength(100);
            entity.Property(e => e.UserAgent).HasMaxLength(256);
            entity.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Restrict);
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
            entity.HasOne(e => e.Owner).WithMany().HasForeignKey(e => e.OwnerUserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(e => new { e.OwnerUserId, e.TenantId });
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
            entity.HasOne(e => e.ResponsibleUser).WithMany().HasForeignKey(e => e.ResponsibleUserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(e => new { e.TenantId, e.ResponsibleUserId, e.DeletedAt });
            entity.HasOne(e => e.Owner).WithMany().HasForeignKey(e => e.OwnerUserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(e => new { e.OwnerUserId, e.TenantId });
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
            entity.Property(e => e.AutoRenews).HasDefaultValue(false);
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
            entity.Property(e => e.Description).HasMaxLength(2000);
            entity.Property(e => e.Category).HasMaxLength(100);
            entity.HasOne(e => e.Tenant).WithMany().HasForeignKey(e => e.TenantId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(e => new { e.TenantId, e.Scope });
            entity.HasIndex(e => new { e.UserId, e.Scope });
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

        modelBuilder.Entity<DocumentLink>(entity =>
        {
            entity.HasKey(e => new { e.DocumentId, e.ContractId });
            entity.Property(e => e.CreatedBy).HasMaxLength(500);
            entity.Property(e => e.ModifiedBy).HasMaxLength(500);
            entity.HasOne(e => e.Document).WithMany(d => d.Links).HasForeignKey(e => e.DocumentId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Contract).WithMany().HasForeignKey(e => e.ContractId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<Invoice>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            // Preserve the decimal exactly across all supported database providers.
            entity.Property(e => e.Amount).HasConversion<string>().HasMaxLength(64);
            entity.Property(e => e.Currency).HasMaxLength(3);
            entity.Property(e => e.InvoiceNumber).HasMaxLength(100);
            entity.Property(e => e.Description).HasMaxLength(2000);
            entity.Property(e => e.CreatedBy).HasMaxLength(500);
            entity.Property(e => e.ModifiedBy).HasMaxLength(500);
            entity.HasOne(e => e.Tenant).WithMany().HasForeignKey(e => e.TenantId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Contract).WithMany().HasForeignKey(e => e.ContractId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Document).WithMany().HasForeignKey(e => e.DocumentId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(e => new { e.TenantId, e.InvoiceDate });
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
            // Source addresses are optional, unbounded text. Keep partial and
            // long historical values intact; API input limits apply to new edits.
            entity.Property(e => e.Street).IsRequired(false);
            entity.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Restrict);
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
