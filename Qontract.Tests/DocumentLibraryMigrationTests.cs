using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Qontract.Domain.Models;
using Qontract.Infrastructure.Data;
using Xunit;

namespace Qontract.Tests;

public class DocumentLibraryMigrationTests
{
    [Fact]
    public async Task UpgradePreservesExistingOwnersAndUnknownOwners_AndBackfillsOnlyEstablishedTenancy()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        var migrator = db.GetService<IMigrator>(); await migrator.MigrateAsync("20261001214026_AddInvoicesAndDocumentLinks");
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO Tenants (Id, Name, Type, IsActive, CreatedAt, ModifiedAt) VALUES (1, 'Legacy family', 0, 1, '2025-01-01', '2025-01-01');
            INSERT INTO Users (Id, Name, Email, NormalizedEmail, PasswordHash, Role, TenantId, IsActive, CreatedAt, ModifiedAt)
            VALUES (1, 'Owner', 'owner@example.com', 'OWNER@EXAMPLE.COM', 'unchanged', 0, 1, 1, '2025-01-01', '2025-01-01');
            INSERT INTO Contracts (Id, Title, Status, TenantId, IsActive, CreatedAt, ModifiedAt)
            VALUES (1, 'Known', 1, 1, 1, '2025-01-01', '2025-01-01'), (2, 'Unresolved', 1, NULL, 1, '2025-01-01', '2025-01-01');
            INSERT INTO Documents (Id, ContractId, UserId, FileName, FileType, FilePath, FileSize, UploadedBy, UploadedAt, Version, IsDeleted, CreatedAt, ModifiedAt, CreatedBy)
            VALUES (1, 1, 1, 'Owned', 'text/plain', 'unchanged/owned', 1, 'historic actor', '2025-01-01', 1, 0, '2025-01-01', '2025-02-01', 'original actor'),
                   (2, 1, NULL, 'Unknown owner', 'text/plain', 'unchanged/unknown', 1, 'owner@example.com', '2025-01-01', 1, 0, '2025-01-01', '2025-02-01', 'owner@example.com'),
                   (3, 2, NULL, 'Unresolved', 'text/plain', 'unchanged/unresolved', 1, 'unknown', '2025-01-01', 1, 0, '2025-01-01', '2025-02-01', 'unknown');
            """);
        await migrator.MigrateAsync();
        var rows = await db.Documents.OrderBy(d => d.Id).ToListAsync();
        rows[0].UserId.Should().Be(1); rows[0].TenantId.Should().Be(1);
        rows[0].FilePath.Should().Be("unchanged/owned"); rows[0].CreatedBy.Should().Be("original actor");
        rows[0].ModifiedAt.Should().Be(new DateTime(2025, 2, 1)); rows[0].Scope.Should().Be(DocumentScope.Tenant);
        rows[1].UserId.Should().BeNull(); rows[1].TenantId.Should().Be(1);
        rows[2].UserId.Should().BeNull(); rows[2].TenantId.Should().BeNull(); rows[2].ContractId.Should().Be(2);
        db.Model.FindEntityType(typeof(Document))!.FindProperty("UserId")!.IsShadowProperty().Should().BeFalse();
    }
}
