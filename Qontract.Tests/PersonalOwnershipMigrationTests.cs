using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Qontract.Domain.Models;
using Qontract.Infrastructure.Data;
using Xunit;

namespace Qontract.Tests;

public class PersonalOwnershipMigrationTests
{
    private const string Previous = "20261004164944_AddPasswordReset";

    [Theory]
    [InlineData("sqlite", "Data Source=unused", "INTEGER")]
    [InlineData("postgresql", "Host=localhost;Database=unused", "integer")]
    [InlineData("sqlserver", "Server=localhost;Database=unused", "int")]
    public void UpgradeGeneratesForEachProvider(string provider, string connection, string integerType)
    {
        var builder = new DbContextOptionsBuilder<ApplicationDbContext>();
        builder.Configure(provider, connection);
        using var db = new ApplicationDbContext(builder.Options);
        var script = db.GetService<IMigrator>().GenerateScript(Previous);
        script.Should().Contain("OwnerUserId").And.Contain("FK_Contracts_Users_OwnerUserId");
        script.Should().Contain(integerType);
        if (provider != "sqlite") script.Should().NotContain("INTEGER");
        var downgrade = () => db.GetService<IMigrator>().GenerateScript("20261004203625_PersonalRecordOwnership", Previous);
        downgrade.Should().Throw<NotSupportedException>().WithMessage("*Restore*backup*");
    }

    [Fact]
    public async Task UpgradePreservesSharedRowsAndReferences_AndDoesNotInferOwnersFromAuditOrUserId()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(Previous);
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO Tenants (Id, Name, Type, IsActive, CreatedAt, ModifiedAt)
            VALUES (10, 'Existing tenant', 0, 1, '2025-01-01', '2025-01-01');
            INSERT INTO Users (Id, Name, Email, NormalizedEmail, PasswordHash, Role, IsActive, TenantId, AuthVersion, RequiresPasswordSetup, CreatedAt, ModifiedAt)
            VALUES (11, 'Existing owner', 'owner@example.test', 'OWNER@EXAMPLE.TEST', 'unchanged', 0, 1, 10, 0, 0, '2025-01-01', '2025-01-01');
            INSERT INTO Contractors (Id, Name, Type, TenantId, UserId, IsActive, CreatedAt, ModifiedAt)
            VALUES (12, 'Existing supplier', 1, 10, 11, 1, '2025-01-01', '2025-01-01');
            INSERT INTO Contracts (Id, Title, Status, TenantId, ContractorId, IsActive, CreatedBy, CreatedAt, ModifiedAt)
            VALUES (13, 'Existing shared contract', 1, 10, 12, 1, 'owner@example.test', '2025-01-01', '2025-01-01');
            INSERT INTO Contracts (Id, Title, Status, IsActive, CreatedBy, CreatedAt, ModifiedAt)
            VALUES (14, 'Unresolved old row', 1, 1, 'owner@example.test', '2025-01-01', '2025-01-01');
            """);
        await migrator.MigrateAsync();
        var shared = await db.Contracts.SingleAsync(c => c.Id == 13);
        shared.TenantId.Should().Be(10);
        shared.ContractorId.Should().Be(12);
        shared.OwnerUserId.Should().BeNull();
        shared.CreatedAt.Should().Be(new DateTime(2025, 1, 1));
        (await db.Contracts.FindAsync(14))!.OwnerUserId.Should().BeNull();
        var supplier = (await db.Contractors.FindAsync(12))!;
        supplier.UserId.Should().Be(11);
        supplier.OwnerUserId.Should().BeNull();
        (await db.Users.FindAsync(11))!.PasswordHash.Should().Be("unchanged");
        var personal = new User { Name = "No tenant", Email = "private@example.test", PasswordHash = "setup-required", RequiresPasswordSetup = true, IsActive = true };
        db.Users.Add(personal);
        await db.SaveChangesAsync();
        db.Contractors.Add(new Contractor { Name = "Private supplier", OwnerUserId = personal.Id, IsActive = true });
        db.Contracts.Add(new Contract { Title = "Private contract", OwnerUserId = personal.Id, IsActive = true });
        await db.SaveChangesAsync();
        (await db.TenantMemberships.AnyAsync(m => m.UserId == personal.Id)).Should().BeFalse();
        await migrator.MigrateAsync();
        (await db.Contracts.CountAsync()).Should().Be(3);
    }
}
