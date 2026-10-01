using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Qontract.Infrastructure.Data;
using Xunit;

namespace Qontract.Tests;

public class InvoiceMigrationTests
{
    [Fact]
    public async Task UpgradeKeepsExistingDocumentStorageAndPrimaryAssociation()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261001210440_AddBillingPeriodsAndPriceHistory");
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO Contracts (Id, Title, Status, IsActive, CreatedAt, ModifiedAt)
            VALUES (1, 'Existing', 1, 1, '2025-01-01', '2025-02-01');
            INSERT INTO Documents (Id, ContractId, FileName, FileType, FilePath, FileSize, UploadedBy, UploadedAt, Version, CreatedAt, ModifiedAt, IsDeleted)
            VALUES (1, 1, 'existing.pdf', 'application/pdf', 'legacy/path.pdf', 42, 'legacy@example.com', '2025-01-01', 1, '2025-01-01', '2025-02-01', 0);
            """);
        await migrator.MigrateAsync();
        var document = await db.Documents.SingleAsync();
        document.ContractId.Should().Be(1); document.FilePath.Should().Be("legacy/path.pdf");
        document.UploadedBy.Should().Be("legacy@example.com"); document.CreatedAt.Should().Be(new DateTime(2025, 1, 1));
        (await db.DocumentLinks.CountAsync()).Should().Be(0);
        (await db.Invoices.CountAsync()).Should().Be(0);
    }
}
