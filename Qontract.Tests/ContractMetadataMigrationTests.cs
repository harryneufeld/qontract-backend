using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Qontract.Infrastructure.Data;
using Xunit;

namespace Qontract.Tests;

public class ContractMetadataMigrationTests
{
    [Fact]
    public async Task UpgradePreservesExistingContractAndLeavesLegacyMetadataUnknown()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261002064535_AddDocumentLibrariesAndOwnership");
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO Contracts (Id, Title, Status, Value, IsActive, CreatedAt, ModifiedAt, CreatedBy)
            VALUES (1, 'Existing', 1, 'unchanged', 1, '2025-01-01', '2025-02-01', 'original actor');
            """);
        await migrator.MigrateAsync();
        var row = await db.Contracts.SingleAsync();
        row.Type.Should().BeNull(); row.Iban.Should().BeNull(); row.Notes.Should().BeNull();
        row.Title.Should().Be("Existing"); row.Value.Should().Be("unchanged"); row.IsActive.Should().BeTrue();
        row.CreatedBy.Should().Be("original actor"); row.ModifiedAt.Should().Be(new DateTime(2025, 2, 1));
        (await db.ContractPrices.CountAsync()).Should().Be(0);
    }
}
