using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Qontract.Infrastructure.Data;
using Xunit;

namespace Qontract.Tests;

public class ContractReviewMigrationTests
{
    [Fact]
    public async Task UpgradePreservesProvenLegacyInactivityAndHidesOldDeletions()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261004211837_AccountAndTenantAddresses");
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO Tenants (Id,Name,Type,IsActive,CreatedAt,ModifiedAt) VALUES (10,'Tenant',0,1,'2025-01-01','2025-01-01');
            INSERT INTO Users (Id,Name,Email,NormalizedEmail,PasswordHash,Role,IsActive,TenantId,AuthVersion,RequiresPasswordSetup,CreatedAt,ModifiedAt)
                VALUES (11,'Member','member@example.test','MEMBER@EXAMPLE.TEST','unchanged',0,1,10,0,0,'2025-01-01','2025-01-01');
            INSERT INTO TenantMemberships (UserId,TenantId,Role,IsActive,CreatedAt,ModifiedAt) VALUES (11,10,0,1,'2025-01-01','2025-01-01');
            INSERT INTO Contracts (Id,Title,Status,IsActive,TenantId,CreatedAt,ModifiedAt)
                VALUES (1,'Migrated inactive',1,0,10,'2025-01-01','2025-01-01'),(2,'Deleted before upgrade',1,0,10,'2025-01-01','2025-01-01');
            CREATE TABLE LocalSourceLedger (Entity TEXT NOT NULL,SourceId TEXT NOT NULL,TargetReference TEXT,RawJson TEXT NOT NULL,PRIMARY KEY(Entity,SourceId));
            """);
        await db.Database.ExecuteSqlRawAsync("INSERT INTO LocalSourceLedger VALUES ({0},{1},{2},{3})", "Users", "source-user", "11", "{}");
        await db.Database.ExecuteSqlRawAsync("INSERT INTO LocalSourceLedger VALUES ({0},{1},{2},{3})", "Contracts", "source-contract", "1", "{\"IsActive\":0,\"UserId\":\"source-user\"}");
        await migrator.MigrateAsync();
        var inactive = await db.Contracts.SingleAsync(c => c.Id == 1);
        inactive.IsActive.Should().BeFalse();
        inactive.DeletedAt.Should().BeNull();
        inactive.ResponsibleUserId.Should().Be(11);
        var deleted = await db.Contracts.SingleAsync(c => c.Id == 2);
        deleted.DeletedAt.Should().NotBeNull();
        deleted.RetentionExpiresAt.Should().BeAfter(DateTime.UtcNow.AddDays(89));
        deleted.ResponsibleUserId.Should().BeNull("audit identity is not responsibility evidence");
    }
}
