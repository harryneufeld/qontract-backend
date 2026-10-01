using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Qontract.Infrastructure.Data;
using Xunit;

namespace Qontract.Tests;

public class BillingMigrationTests
{
    [Fact]
    public async Task UpgradePreservesExistingValueWithoutInventingPeriodOrHistory()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261001201911_AddRegistrationAndInvitations");
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO Contracts (Id, Title, Status, Value, Currency, IsActive, CreatedAt, ModifiedAt)
            VALUES (1, 'Existing contract', 1, '1.234,56 EUR', 'EUR', 1, '2025-01-01', '2025-02-01');
            """);
        await migrator.MigrateAsync();
        var contract = await db.Contracts.SingleAsync();
        contract.Value.Should().Be("1.234,56 EUR");
        contract.BillingPeriod.Should().BeNull();
        contract.CreatedAt.Should().Be(new DateTime(2025, 1, 1));
        (await db.ContractPrices.CountAsync()).Should().Be(0);
    }
}
