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
    public DbSet<Contractor> Contractors { get; set; }
    public DbSet<Contract> Contracts { get; set; }
    public DbSet<Document> Documents { get; set; }
    public DbSet<Address> Addresses { get; set; }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ApplyAuditTimestamps();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ApplyAuditTimestamps();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>
    /// Maintains the audit timestamps on every saved entity (issue #16):
    /// new entities get their CreatedAt/ModifiedAt stamped on first save and
    /// ModifiedAt is refreshed on every update.
    /// </summary>
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
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Email).IsRequired().HasMaxLength(100);
            entity.Property(e => e.PasswordHash).IsRequired().HasMaxLength(255);
            entity.Property(e => e.Role).IsRequired();
            entity.Property(e => e.TenantId).IsRequired();
            entity.Property(e => e.CreatedBy).HasMaxLength(500);
            entity.Property(e => e.ModifiedBy).HasMaxLength(500);
            entity.HasOne(e => e.Tenant)
                .WithMany()
                .HasForeignKey(e => e.TenantId);
        });

        // Contractor
        modelBuilder.Entity<Contractor>(entity =>
        {
            entity.ToTable("Contractors");
            entity.HasKey(e => e.Id);
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

        // Document
        modelBuilder.Entity<Document>(entity =>
        {
            entity.ToTable("Documents");
            entity.HasKey(e => e.Id);
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
