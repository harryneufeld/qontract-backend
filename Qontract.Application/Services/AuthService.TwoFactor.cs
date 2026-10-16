using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Qontract.Application.Models;
using Qontract.Core.Security;

namespace Qontract.Application.Services;

public partial class AuthService
{
    public async Task<TwoFactorStatusDTO> TwoFactorStatusAsync(int userId, CancellationToken ct = default)
    {
        var user = await Account(userId, ct);
        return new(user.TwoFactorEnabled, Hashes(user).Length);
    }

    public async Task<TwoFactorSetupDTO> SetupTwoFactorAsync(int userId, string password, CancellationToken ct = default)
    {
        var user = await ProvePassword(userId, password, ct);
        if (user.TwoFactorEnabled) throw new InvalidOperationException("Two-factor authentication is already enabled.");
        var secret = Totp.NewSecret();
        var encrypted = Protector.Protect(secret);
        var expires = DateTime.UtcNow.AddMinutes(10);
        var updated = await _context.Users.Where(u => u.Id == userId && u.AuthVersion == user.AuthVersion
                && u.PasswordHash == user.PasswordHash && !u.TwoFactorEnabled && u.IsActive && !u.RequiresPasswordSetup)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.PendingTwoFactorSecret, encrypted)
                .SetProperty(u => u.TwoFactorSetupExpiresAt, expires), ct);
        if (updated != 1) throw new InvalidOperationException("Account changed. Please sign in again.");
        return new(secret, $"otpauth://totp/{Uri.EscapeDataString("qontract:" + user.Email)}?secret={secret}&issuer=qontract&algorithm=SHA1&digits=6&period=30");
    }

    public async Task<string[]> EnableTwoFactorAsync(int userId, TwoFactorProofDTO proof, CancellationToken ct = default)
    {
        var user = await ProvePassword(userId, proof.Password, ct);
        if (user.TwoFactorEnabled || user.PendingTwoFactorSecret == null || user.TwoFactorSetupExpiresAt <= DateTime.UtcNow
            || user.TwoFactorSetupExpiresAt == null) throw new InvalidOperationException("Restart two-factor setup.");
        await ClaimFactorAttempt(userId, ct);
        var step = Totp.Verify(Protector.Unprotect(user.PendingTwoFactorSecret), proof.Code, -1, DateTimeOffset.UtcNow);
        if (step == null || proof.UseRecoveryCode)
            throw new InvalidOperationException("Invalid or already used two-factor code. Try a new code or a recovery code.");
        var codes = Enumerable.Range(0, 10).Select(_ => Convert.ToHexString(RandomNumberGenerator.GetBytes(10))).ToArray();
        var hashes = JsonSerializer.Serialize(codes.Select(RecoveryHash));
        var now = DateTime.UtcNow;
        var updated = await _context.Users.Where(u => u.Id == userId && u.AuthVersion == user.AuthVersion
                && u.PendingTwoFactorSecret == user.PendingTwoFactorSecret && !u.TwoFactorEnabled && u.IsActive && !u.RequiresPasswordSetup
                && u.TwoFactorSetupExpiresAt > now)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.TwoFactorEnabled, true)
                .SetProperty(u => u.TwoFactorSecret, user.PendingTwoFactorSecret)
                .SetProperty(u => u.PendingTwoFactorSecret, (string?)null)
                .SetProperty(u => u.TwoFactorSetupExpiresAt, (DateTime?)null)
                .SetProperty(u => u.TwoFactorRecoveryHashes, hashes)
                .SetProperty(u => u.TwoFactorLastStep, step.Value)
                .SetProperty(u => u.TwoFactorFailures, 0)
                .SetProperty(u => u.TwoFactorLockedUntil, (DateTime?)null)
                .SetProperty(u => u.AuthVersion, u => u.AuthVersion + 1), ct);
        if (updated != 1) throw new InvalidOperationException("Account changed. Please sign in again.");
        return codes;
    }

    public async Task DisableTwoFactorAsync(int userId, TwoFactorProofDTO proof, CancellationToken ct = default)
    {
        var user = await ProvePassword(userId, proof.Password, ct);
        if (!user.TwoFactorEnabled || !await ConsumeSecondFactor(user, proof.Code, proof.UseRecoveryCode, ct))
            throw new InvalidOperationException("Invalid or already used two-factor code. Try a new code or a recovery code.");
        var updated = await _context.Users.Where(u => u.Id == userId && u.AuthVersion == user.AuthVersion && u.IsActive)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.TwoFactorEnabled, false)
                .SetProperty(u => u.TwoFactorSecret, (string?)null)
                .SetProperty(u => u.PendingTwoFactorSecret, (string?)null)
                .SetProperty(u => u.TwoFactorSetupExpiresAt, (DateTime?)null)
                .SetProperty(u => u.TwoFactorRecoveryHashes, "[]")
                .SetProperty(u => u.TwoFactorLastStep, -1L)
                .SetProperty(u => u.AuthVersion, u => u.AuthVersion + 1), ct);
        if (updated != 1) throw new InvalidOperationException("Account changed. Please sign in again.");
    }

    private ITwoFactorSecretProtector Protector => _twoFactorProtector
        ?? throw new InvalidOperationException("Two-factor secret protection is unavailable.");
    private async Task<User> Account(int userId, CancellationToken ct) =>
        await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId && u.IsActive && !u.RequiresPasswordSetup, ct)
        ?? throw new InvalidOperationException("Account unavailable.");

    private async Task<User> ProvePassword(int userId, string password, CancellationToken ct)
    {
        var user = await Account(userId, ct);
        if (!VerifyPassword(password, user.PasswordHash)) throw new InvalidOperationException("Current password is incorrect.");
        return user;
    }

    private static string[] Hashes(User user) => JsonSerializer.Deserialize<string[]>(user.TwoFactorRecoveryHashes) ?? [];
    private static string RecoveryHash(string code) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(code.Replace("-", "").Replace(" ", "").ToUpperInvariant())));
    private const int FreeFactorAttempts = 4;
    private static readonly TimeSpan MaxFactorLock = TimeSpan.FromHours(24);

    /// <summary>Lock after the given number of consecutive unsuccessful attempts:
    /// none for the first four, then 5 minutes doubling per attempt up to 24 hours.</summary>
    public static TimeSpan? FactorLockDuration(int failures) => failures <= FreeFactorAttempts ? null
        : TimeSpan.FromMinutes(Math.Min(5 * Math.Pow(2, failures - FreeFactorAttempts - 1), MaxFactorLock.TotalMinutes));

    private static InvalidOperationException FactorLocked() =>
        new("Too many two-factor attempts. Please try again later.");

    // Every factor check reserves its attempt (and applies the resulting lock)
    // before the code is verified, so simultaneous requests cannot test more
    // codes than the lockout allows. Only a successful check resets the count;
    // locks therefore escalate instead of granting a fresh budget on expiry.
    private async Task ClaimFactorAttempt(int userId, CancellationToken ct)
    {
        for (var round = 0; round < 20; round++)
        {
            var state = await _context.Users.AsNoTracking().Where(u => u.Id == userId)
                .Select(u => new { u.TwoFactorFailures, u.TwoFactorLockedUntil }).SingleAsync(ct);
            var now = DateTime.UtcNow;
            if (state.TwoFactorLockedUntil > now) throw FactorLocked();
            var failures = state.TwoFactorFailures + 1;
            DateTime? until = now + FactorLockDuration(failures);
            var claimed = await _context.Users.Where(u => u.Id == userId && u.TwoFactorFailures == state.TwoFactorFailures
                    && (u.TwoFactorLockedUntil == null || u.TwoFactorLockedUntil <= now))
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.TwoFactorFailures, failures)
                    .SetProperty(u => u.TwoFactorLockedUntil, until), ct);
            if (claimed == 1) return;
        }
        throw FactorLocked();
    }

    private async Task<bool> ConsumeSecondFactor(User user, string code, bool recovery, CancellationToken ct)
    {
        await ClaimFactorAttempt(user.Id, ct);
        var hashes = Hashes(user);
        var hash = RecoveryHash(code);
        long? step = recovery ? null : Totp.Verify(Protector.Unprotect(user.TwoFactorSecret!), code,
            user.TwoFactorLastStep, DateTimeOffset.UtcNow);
        if (recovery ? !hashes.Contains(hash) : step == null) return false;
        var remaining = recovery ? JsonSerializer.Serialize(hashes.Where(h => h != hash)) : user.TwoFactorRecoveryHashes;
        // Compare-and-swap makes recovery codes and TOTP time steps single use,
        // even for simultaneous requests. Credential changes invalidate the proof.
        // The lock is not rechecked: this attempt was already admitted by its claim.
        return await _context.Users.Where(u => u.Id == user.Id && u.IsActive && !u.RequiresPasswordSetup
                && u.AuthVersion == user.AuthVersion && u.TwoFactorEnabled && u.TwoFactorSecret == user.TwoFactorSecret
                && u.TwoFactorLastStep == user.TwoFactorLastStep && u.TwoFactorRecoveryHashes == user.TwoFactorRecoveryHashes)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.TwoFactorLastStep, step ?? user.TwoFactorLastStep)
                .SetProperty(u => u.TwoFactorRecoveryHashes, remaining).SetProperty(u => u.TwoFactorFailures, 0)
                .SetProperty(u => u.TwoFactorLockedUntil, (DateTime?)null), ct) == 1;
    }
}
