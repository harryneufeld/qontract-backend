using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Qontract.Infrastructure.Data;
using Xunit;

namespace Qontract.Tests;

public class TenantMembershipMigrationTests
{
    [Theory]
    [InlineData("sqlite", "Data Source=unused")]
    // PostgreSQL has its own chain (Qontract.Infrastructure.PostgreSql).
    [InlineData("sqlserver", "Server=localhost;Database=unused")]
    public void MembershipMigration_GeneratesForEachSupportedProvider(string provider, string connection)
    {
        var builder = new DbContextOptionsBuilder<ApplicationDbContext>();
        builder.Configure(provider, connection);
        using var db = new ApplicationDbContext(builder.Options);
        var script = db.GetService<IMigrator>().GenerateScript(
            "20260924224824_AddDocumentSupersededById");
        script.Should().Contain("TenantMemberships");
        script.Should().Contain("INSERT INTO");
        script.Should().Contain("FK_TenantMemberships_Users_UserId");
        script.Should().Contain("TenantInvitations");
        script.Should().Contain("IX_Users_NormalizedEmail");
        script.Should().Contain("ContractPrices");
        script.Should().Contain("BillingPeriod");
        script.Should().Contain("Invoices");
        script.Should().Contain("DocumentLinks");
        script.Should().Contain("IX_Documents_UserId_Scope");
        script.Should().Contain("IX_Documents_TenantId_Scope");
        script.Should().Contain("Iban");
        script.Should().Contain("Website");
        script.Should().Contain("Notes");
    }

    [Fact]
    public async Task Upgrade_BackfillsExistingRolesAndActiveState_WithoutChangingAccounts()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var db = new ApplicationDbContext(options);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20260924224824_AddDocumentSupersededById");
        // Insert against the old schema: the new-model write hook must not
        // provide the membership that this test expects the migration to add.
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO Tenants (Id, Name, Type, IsActive, CreatedAt, ModifiedAt)
            VALUES (10, 'Existing family', 0, 1, '2025-01-01', '2025-02-01');
            INSERT INTO Users (Id, Name, Email, PasswordHash, Role, IsActive, TenantId, CreatedAt, ModifiedAt, CreatedBy)
            VALUES (11, 'Existing admin', 'existing@example.com', 'unchanged-hash', 0, 1, 10, '2025-01-01', '2025-02-01', 'legacy');
            INSERT INTO Users (Id, Name, Email, PasswordHash, Role, IsActive, TenantId, CreatedAt, ModifiedAt)
            VALUES (12, 'Inactive member', 'inactive@example.com', 'other-hash', 1, 0, 10, '2025-01-01', '2025-02-01');
            """);

        await migrator.MigrateAsync();
        var memberships = await db.TenantMemberships.OrderBy(m => m.UserId).ToListAsync();
        memberships.Should().HaveCount(2);
        memberships[0].TenantId.Should().Be(10);
        memberships[0].Role.Should().Be(Qontract.Domain.Models.UserRole.TenantAdmin);
        memberships[0].IsActive.Should().BeTrue();
        memberships[0].CreatedBy.Should().Be("legacy");
        memberships[0].CreatedAt.Should().Be(new DateTime(2025, 1, 1));
        memberships[1].IsActive.Should().BeFalse();
        memberships[1].Role.Should().Be(Qontract.Domain.Models.UserRole.User);
        (await db.Users.FindAsync(11))!.PasswordHash.Should().Be("unchanged-hash");
        (await db.Users.FindAsync(11))!.NormalizedEmail.Should().Be("EXISTING@EXAMPLE.COM");

        // Reapplying is a no-op rather than a duplicate backfill.
        await migrator.MigrateAsync();
        (await db.TenantMemberships.CountAsync()).Should().Be(2);
    }
}
