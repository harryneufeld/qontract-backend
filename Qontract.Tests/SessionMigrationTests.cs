using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Qontract.Infrastructure.Data;

namespace Qontract.Tests;

public class SessionMigrationTests
{
    [Fact]
    public async Task UpgradePreservesUsers_WithoutCreatingSessionsForOldTokens()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261002083437_AddContractorLegacyMetadata");
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO Tenants (Id, Name, Type, IsActive, CreatedAt, ModifiedAt) VALUES (1, 'Existing', 0, 1, '2025-01-01', '2025-01-01');
            INSERT INTO Users (Id, Name, Email, NormalizedEmail, PasswordHash, Role, TenantId, IsActive, CreatedAt, ModifiedAt)
            VALUES (1, 'Existing user', 'user@example.com', 'USER@EXAMPLE.COM', 'existing hash', 1, 1, 1, '2025-01-01', '2025-02-01');
            """);
        await migrator.MigrateAsync();
        var user = await db.Users.SingleAsync();
        user.AuthVersion.Should().Be(0); user.PasswordHash.Should().Be("existing hash");
        user.Email.Should().Be("user@example.com"); user.TenantId.Should().Be(1);
        user.ModifiedAt.Should().Be(new DateTime(2025, 2, 1));
        (await db.AuthSessions.CountAsync()).Should().Be(0);
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("postgres")]
    [InlineData("sqlserver")]
    public void GeneratesProviderSpecificSessionSchema(string provider)
    {
        var builder = new DbContextOptionsBuilder<ApplicationDbContext>();
        if (provider == "sqlite") builder.UseSqlite("Data Source=:memory:");
        if (provider == "postgres") builder.UseNpgsql("Host=localhost;Database=unused");
        if (provider == "sqlserver") builder.UseSqlServer("Server=localhost;Database=unused;Integrated Security=true");
        using var db = new ApplicationDbContext(builder.Options);
        var script = db.GetService<IMigrator>().GenerateScript("20261002083437_AddContractorLegacyMetadata");
        script.Should().Contain("AuthSessions").And.Contain("AuthVersion").And.Contain("RevokedAt");
        if (provider == "postgres") script.Should().Contain("uuid").And.Contain("timestamp");
        if (provider == "sqlserver") script.Should().Contain("uniqueidentifier").And.Contain("datetime2");
    }
}
