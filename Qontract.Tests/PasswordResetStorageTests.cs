using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Qontract.Application.Interfaces;
using Qontract.Application.Services;
using Qontract.Domain.Models;
using Qontract.Infrastructure.Data;

namespace Qontract.Tests;
public class PasswordResetStorageTests
{
    [Fact]
    public async Task TwoConnectionsCannotRedeemOneTokenTwice()
    {
        var path = Path.GetTempFileName();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite($"Data Source={path};Pooling=False").Options;
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["PasswordReset:Enabled"] = "true" }).Build();
        try
        {
            int userId;
            await using (var seed = new ApplicationDbContext(options))
            {
                await seed.Database.EnsureCreatedAsync();
                var tenant = new Tenant { Name = "Copy", IsActive = true }; seed.Tenants.Add(tenant); await seed.SaveChangesAsync();
                var user = new User { Name = "User", Email = "user@example.test", TenantId = tenant.Id, IsActive = true, PasswordHash = "unusable" };
                seed.Users.Add(user); await seed.SaveChangesAsync(); userId = user.Id;
                seed.PasswordResets.Add(new PasswordReset { UserId = userId, NormalizedEmail = user.NormalizedEmail,
                    TokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))),
                    CreatedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddMinutes(30) });
                await seed.SaveChangesAsync();
            }
            using var start = new Barrier(2);
            async Task<bool> Attempt()
            {
                await using var db = new ApplicationDbContext(options);
                var service = new PasswordResetService(db, config, new NeverSend());
                start.SignalAndWait();
                return await service.RedeemAsync(token, "replacement123");
            }
            var results = await Task.WhenAll(Task.Run(Attempt), Task.Run(Attempt));
            results.Count(r => r).Should().Be(1);
            await using var verify = new ApplicationDbContext(options);
            (await verify.Users.SingleAsync(u => u.Id == userId)).AuthVersion.Should().Be(1);
            (await verify.PasswordResets.SingleAsync()).ConsumedAt.Should().NotBeNull();
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task MigrationPreservesExistingAccountAndAudit_WithoutRequiringReset()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        var migrator = db.GetService<IMigrator>(); await migrator.MigrateAsync("20261004162808_AddRevocableAuthSessions");
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO Tenants (Id, Name, Type, IsActive, CreatedAt, ModifiedAt) VALUES (1, 'Existing', 0, 1, '2025-01-01', '2025-01-01');
            INSERT INTO Users (Id, Name, Email, NormalizedEmail, PasswordHash, Role, TenantId, IsActive, CreatedAt, ModifiedAt, AuthVersion)
            VALUES (1, 'User', 'user@example.test', 'USER@EXAMPLE.TEST', 'existing hash', 1, 1, 1, '2025-01-01', '2025-02-01', 3);
            """);
        await migrator.MigrateAsync(); var user = await db.Users.SingleAsync();
        user.RequiresPasswordSetup.Should().BeFalse(); user.AuthVersion.Should().Be(3);
        user.PasswordHash.Should().Be("existing hash"); user.ModifiedAt.Should().Be(new DateTime(2025, 2, 1));
        (await db.PasswordResets.CountAsync()).Should().Be(0);
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("postgres")]
    [InlineData("sqlserver")]
    public void MigrationUsesNativeProviderTypes(string provider)
    {
        var b = new DbContextOptionsBuilder<ApplicationDbContext>();
        if (provider == "sqlite") b.UseSqlite("Data Source=:memory:");
        if (provider == "postgres") b.UseNpgsql("Host=localhost;Database=unused");
        if (provider == "sqlserver") b.UseSqlServer("Server=localhost;Database=unused;Integrated Security=true");
        using var db = new ApplicationDbContext(b.Options);
        var script = db.GetService<IMigrator>().GenerateScript("20261004162808_AddRevocableAuthSessions");
        script.Should().Contain("PasswordResets").And.Contain("RequiresPasswordSetup");
        if (provider == "postgres") script.Should().Contain("uuid").And.Contain("boolean");
        if (provider == "sqlserver") script.Should().Contain("uniqueidentifier").And.Contain("nvarchar(64)").And.Contain("bit");
    }
    private sealed class NeverSend : IResetEmailSender
    { public Task SendAsync(string email, Uri resetLink, CancellationToken ct) => throw new InvalidOperationException("Unexpected email"); }
}
