namespace Qontracts.MultiTenant.Infrastructure
{
    using Microsoft.EntityFrameworkCore;
    using Qontracts.MultiTenant.Domain;

    public class MultiTenantDbContext : DbContext
    {
        public MultiTenantDbContext(DbContextOptions<MultiTenantDbContext> options) : base(options)
        {
        }

        // Core entities
        public DbSet<Tenant> Tenants { get; set; }
        public DbSet<User> Users { get; set; }
        public DbSet<Contractor> Contractors { get; set; }
        public DbSet<Contract> Contracts { get; set; }
        public DbSet<Document> Documents { get; set; }
        public DbSet<Address> Addresses { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Configure Tenant
            modelBuilder.Entity<Tenant>()
                .HasMany(t => t.Users)
                .WithMany(u => u.Tenant)
                .UsingEntity(j => j.HasOne(t => t.Users).WithMany().HasForeignKey(u => u.TenantId).OnDelete(DeleteBehavior.Cascade))
                .HasOne(t => t.Contractors)
                .WithMany(c => c.Tenant)
                .UsingEntity(j => j.HasOne(c => c.Contractors).WithMany().HasForeignKey(u => u.TenantId).OnDelete(DeleteBehavior.Cascade))
                .HasOne(t => t.Contracts)
                .WithMany(c => c.Tenant)
                .UsingEntity(j => j.HasOne(c => c.Contracts).WithMany().HasForeignKey(c => c.TenantId).OnDelete(DeleteBehavior.Cascade));

            // Configure Address (polymorphic inheritance)
            modelBuilder.Entity<Address>()
                .HasIndex(a => new { a.EntityType, a.EntityId })
                .IsUnique();
        }
    }
}
