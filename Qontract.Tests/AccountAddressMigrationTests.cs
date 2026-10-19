using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Qontract.Domain.Models;
using Qontract.Infrastructure.Data;
using Xunit;

namespace Qontract.Tests;

public class AccountAddressMigrationTests
{
    private const string Previous = "20261004203625_PersonalRecordOwnership";

    [Theory]
    [InlineData("sqlite", "Data Source=unused")]
    // PostgreSQL has its own chain (Qontract.Infrastructure.PostgreSql).
    [InlineData("sqlserver", "Server=localhost;Database=unused")]
    public void UpgradeSupportsEachProvider_AndRefusesDestructiveDowngrade(string provider, string connection)
    {
        var builder = new DbContextOptionsBuilder<ApplicationDbContext>(); builder.Configure(provider, connection);
        using var db = new ApplicationDbContext(builder.Options);
        var script = db.GetService<IMigrator>().GenerateScript(Previous);
        script.Should().Contain("HouseNumber").And.Contain("FK_Addresses_Users_UserId");
        if (provider == "sqlserver") script.Should().Contain("nvarchar(max)");
        var downgrade = () => db.GetService<IMigrator>().GenerateScript("20261004211837_AccountAndTenantAddresses", Previous);
        downgrade.Should().Throw<NotSupportedException>().WithMessage("*Restore*backup*");
    }

    [Fact]
    public async Task UpgradePreservesOldAddressesAndAssociations_WithoutSplittingStreetOrGuessingAccountOwner()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        var migrator = db.GetService<IMigrator>(); await migrator.MigrateAsync(Previous);
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO Tenants (Id, Name, Type, IsActive, CreatedAt, ModifiedAt)
            VALUES (10, 'Existing tenant', 0, 1, '2025-01-01', '2025-01-01');
            INSERT INTO Users (Id, Name, Email, NormalizedEmail, PasswordHash, Role, IsActive, AuthVersion, RequiresPasswordSetup, CreatedAt, ModifiedAt)
            VALUES (11, 'Private owner', 'owner@example.test', 'OWNER@EXAMPLE.TEST', 'unchanged', 1, 1, 0, 1, '2025-01-01', '2025-01-01');
            INSERT INTO Contractors (Id, Name, Type, TenantId, IsActive, CreatedAt, ModifiedAt)
            VALUES (12, 'Existing supplier', 1, 10, 1, '2025-01-01', '2025-01-01');
            INSERT INTO Addresses (Id, EntityType, Street, City, Zip, Country, IsActive, ContractorId, CreatedAt, ModifiedAt, CreatedBy)
            VALUES (13, 1, ' Old street 5/a ', 'Berlin', '01234', 'Germany', 1, 12, '2025-01-01', '2025-02-01', 'owner@example.test');
            INSERT INTO Addresses (Id, EntityType, Street, IsActive, TenantId, CreatedAt, ModifiedAt)
            VALUES (14, 0, 'Tenant street 12', 1, 10, '2025-01-01', '2025-01-01');
            INSERT INTO Addresses (Id, EntityType, Street, IsActive, CreatedAt, ModifiedAt, CreatedBy)
            VALUES (15, 2, 'Unknown person street', 0, '2025-01-01', '2025-01-01', 'owner@example.test');
            """);
        await migrator.MigrateAsync();
        var contractor = (await db.Addresses.FindAsync(13))!;
        contractor.Street.Should().Be(" Old street 5/a "); contractor.HouseNumber.Should().BeNull();
        contractor.ContractorId.Should().Be(12); contractor.UserId.Should().BeNull();
        contractor.ModifiedAt.Should().Be(new DateTime(2025, 2, 1));
        (await db.Addresses.FindAsync(14))!.TenantId.Should().Be(10);
        var unresolved = (await db.Addresses.FindAsync(15))!;
        unresolved.UserId.Should().BeNull(); unresolved.IsActive.Should().BeFalse();
        db.Addresses.Add(new Address { EntityType = EntityType.Person, UserId = 11,
            HouseNumber = " 12/a ", City = new string('Ä', 1000) });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var personal = await db.Addresses.SingleAsync(a => a.UserId == 11);
        personal.Street.Should().BeNull(); personal.HouseNumber.Should().Be(" 12/a "); personal.City!.Length.Should().Be(1000);
        await migrator.MigrateAsync(); (await db.Addresses.CountAsync()).Should().Be(4);
    }
}
