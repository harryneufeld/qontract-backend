using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Qontract.Application.Models;
using Qontract.Application.Services;
using Qontract.Core.Security;
using Qontract.Domain.Models;
using Qontract.Infrastructure.Data;

namespace Qontract.Tests;

public class TwoFactorConcurrencyTests
{
    [Fact]
    public async Task TwoConnectionsCannotExchangeOneRecoveryCodeForTwoSessions()
    {
        var path = Path.GetTempFileName();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite($"Data Source={path};Pooling=False").Options;
        const string code = "0123456789ABCDEF0123";
        try
        {
            await using (var db = new ApplicationDbContext(options))
            {
                await db.Database.EnsureCreatedAsync();
                db.Users.Add(new User { Name = "Concurrent", Email = "race@example.test", PasswordHash = Passwords.Hash("password123"),
                    IsActive = true, TwoFactorEnabled = true, TwoFactorSecret = "unused-for-recovery",
                    TwoFactorRecoveryHashes = JsonSerializer.Serialize(new[] { Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code))) }) });
                await db.SaveChangesAsync();
            }
            using var start = new Barrier(2);
            async Task<bool> Attempt()
            {
                await using var db = new ApplicationDbContext(options);
                var service = new AuthService(db, new ConfigurationBuilder().Build(), NullLogger<AuthService>.Instance);
                start.SignalAndWait();
                try
                {
                    var result = await service.LoginAsync(new LoginDTO { Email = "race@example.test", Password = "password123",
                        TwoFactorCode = code, UseRecoveryCode = true });
                    return !string.IsNullOrEmpty(result.AccessToken);
                }
                catch (InvalidOperationException) { return false; }
            }
            var results = await Task.WhenAll(Task.Run(Attempt), Task.Run(Attempt));
            results.Count(ok => ok).Should().Be(1);
            await using var verify = new ApplicationDbContext(options);
            (await verify.AuthSessions.CountAsync()).Should().Be(1);
            (await verify.Users.SingleAsync()).TwoFactorRecoveryHashes.Should().Be("[]");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task SimultaneousGuessesCannotExceedTheLockoutBudget()
    {
        var path = Path.GetTempFileName();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite($"Data Source={path};Pooling=False").Options;
        try
        {
            await using (var db = new ApplicationDbContext(options))
            {
                await db.Database.EnsureCreatedAsync();
                db.Users.Add(new User { Name = "Burst", Email = "burst@example.test", PasswordHash = Passwords.Hash("password123"),
                    IsActive = true, TwoFactorEnabled = true, TwoFactorSecret = "unused-for-recovery",
                    TwoFactorRecoveryHashes = JsonSerializer.Serialize(new[] { "00" }) });
                await db.SaveChangesAsync();
            }
            const int attempts = 12;
            using var start = new Barrier(attempts);
            async Task Attempt(int n)
            {
                await using var db = new ApplicationDbContext(options);
                var service = new AuthService(db, new ConfigurationBuilder().Build(), NullLogger<AuthService>.Instance);
                start.SignalAndWait();
                try
                {
                    await service.LoginAsync(new LoginDTO { Email = "burst@example.test", Password = "password123",
                        TwoFactorCode = $"WRONG{n}", UseRecoveryCode = true });
                }
                catch (InvalidOperationException) { }
            }
            await Task.WhenAll(Enumerable.Range(0, attempts).Select(n => Task.Run(() => Attempt(n))));
            await using var verify = new ApplicationDbContext(options);
            var user = await verify.Users.SingleAsync();
            // Exactly five guesses were admitted; the fifth applied the lock and
            // every other simultaneous request was refused before verification.
            user.TwoFactorFailures.Should().Be(5);
            user.TwoFactorLockedUntil.Should().BeAfter(DateTime.UtcNow.AddMinutes(4));
        }
        finally { File.Delete(path); }
    }
}
