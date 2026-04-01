using Microsoft.EntityFrameworkCore;
using Qontract.Infrastructure.Data;

namespace Qontract.Infrastructure.Data.Migrations;

public class MigrationsContext : DbContext
{
    public MigrationsContext(DbContextOptions<MigrationsContext> options) : base(options)
    {
    }

    public DbSet<Tenant> Tenants { get; set; }
    public DbSet<User> Users { get; set; }
    public DbSet<Contractor> Contractors { get; set; }
    public DbSet<Contract> Contracts { get; set; }
    public DbSet<Document> Documents { get; set; }
    public DbSet<Address> Addresses { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<Tenant>().ToTable("Tenants");
        modelBuilder.Entity<User>().ToTable("Users");
        modelBuilder.Entity<Contractor>().ToTable("Contractors");
        modelBuilder.Entity<Contract>().ToTable("Contracts");
        modelBuilder.Entity<Document>().ToTable("Documents");
        modelBuilder.Entity<Address>().ToTable("Addresses");
    }
}
