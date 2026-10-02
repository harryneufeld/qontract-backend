using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Qontract.Infrastructure.Data;
using Xunit;

namespace Qontract.Tests;

public class ContractorMetadataMigrationTests
{
    [Fact]
    public async Task UpgradeKeepsExistingContractorAndAudit_WithoutInventingMetadata()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261002073035_AddContractLegacyMetadata");
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO Tenants (Id, Name, Type, IsActive, CreatedAt, ModifiedAt) VALUES (1, 'Legacy family', 0, 1, '2025-01-01', '2025-01-01');
            INSERT INTO Contractors (Id, Name, Type, TenantId, IsActive, Email, Phone, CreatedAt, ModifiedAt, CreatedBy)
            VALUES (1, 'Existing', 1, 1, 0, 'contact@example.com', '+49 123', '2025-01-01', '2025-02-01', 'original actor');
            """);
        await migrator.MigrateAsync();
        var row = await db.Contractors.SingleAsync();
        row.Website.Should().BeNull(); row.Description.Should().BeNull(); row.Notes.Should().BeNull();
        row.Name.Should().Be("Existing"); row.TenantId.Should().Be(1); row.UserId.Should().BeNull(); row.IsActive.Should().BeFalse();
        row.Email.Should().Be("contact@example.com"); row.Phone.Should().Be("+49 123");
        row.CreatedBy.Should().Be("original actor"); row.ModifiedAt.Should().Be(new DateTime(2025, 2, 1));
    }
}
