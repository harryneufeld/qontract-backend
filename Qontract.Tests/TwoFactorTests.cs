using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Qontract.Application.Models;
using Qontract.Core.Security;
using Qontract.Domain.Models;
using Qontract.Infrastructure.Data;

namespace Qontract.Tests;

public class TwoFactorTests : IClassFixture<QontractWebApplicationFactory>
{
    private readonly QontractWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly string _email = $"totp-{Guid.NewGuid():N}@example.com";
    private readonly int _id;
    private const string Password = "password123";

    public TwoFactorTests(QontractWebApplicationFactory factory)
    {
        _factory = factory; _client = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = new User { Name = "2FA user", Email = _email, PasswordHash = Passwords.Hash(Password), IsActive = true };
        db.Users.Add(user); db.SaveChanges(); _id = user.Id;
    }

    private Task<HttpResponseMessage> Login(string? code = null, bool recovery = false, string password = Password) =>
        _client.PostAsJsonAsync("/api/auth/login", new { email = _email, password, twoFactorCode = code, useRecoveryCode = recovery });
    private async Task<AuthDTO> Session() => (await (await Login()).Content.ReadFromJsonAsync<AuthDTO>())!;
    private async Task<HttpResponseMessage> Manage(string token, string action, string code = "", bool recovery = false, string password = Password)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/two-factor/" + action);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new { password, code, useRecoveryCode = recovery });
        return await _client.SendAsync(request);
    }
    private async Task<HttpResponseMessage> Me(string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await _client.SendAsync(request);
    }
    private async Task Update(Action<User> change)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        change(await db.Users.SingleAsync(u => u.Id == _id)); await db.SaveChangesAsync();
    }
    private async Task<(string Token, string Secret, string[] Codes)> Enable()
    {
        var auth = await Session();
        var setup = (await (await Manage(auth.AccessToken, "setup")).Content.ReadFromJsonAsync<TwoFactorSetupDTO>())!;
        var code = Totp.Code(setup.Secret, DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30);
        var response = await Manage(auth.AccessToken, "enable", code);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var codes = (await response.Content.ReadFromJsonAsync<RecoveryResponse>())!.RecoveryCodes;
        return (auth.AccessToken, setup.Secret, codes);
    }
    private record RecoveryResponse(string[] RecoveryCodes);

    [Theory]
    [InlineData(59L, "287082")]
    [InlineData(1111111109L, "081804")]
    [InlineData(1111111111L, "050471")]
    [InlineData(1234567890L, "005924")]
    [InlineData(2000000000L, "279037")]
    [InlineData(20000000000L, "353130")]
    public void Rfc6238Vectors(long seconds, string expected) =>
        Totp.Code("GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ", seconds / 30).Should().Be(expected);

    [Fact]
    public void TotpRejectsMalformedExpiredFutureAndReplayedCodes()
    {
        var secret = Totp.NewSecret(); var now = DateTimeOffset.FromUnixTimeSeconds(6000);
        Totp.Verify(secret, Totp.Code(secret, 200), 199, now).Should().Be(200);
        Totp.Verify(secret, Totp.Code(secret, 200), 200, now).Should().BeNull();
        Totp.Verify(secret, Totp.Code(secret, 198), -1, now).Should().BeNull();
        Totp.Verify(secret, Totp.Code(secret, 202), -1, now).Should().BeNull();
        Totp.Verify(secret, "abcdef", -1, now).Should().BeNull();
    }

    [Fact]
    public async Task EnabledAccountRequiresSecondFactorAndCodesAreSingleUse()
    {
        var enabled = await Enable();
        (await Me(enabled.Token)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var required = (await (await Login()).Content.ReadFromJsonAsync<AuthDTO>())!;
        required.RequiresTwoFactor.Should().BeTrue(); required.AccessToken.Should().BeEmpty(); required.User.Should().BeNull();
        (await Login(password: "wrongpass")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            (await db.AuthSessions.CountAsync(s => s.UserId == _id)).Should().Be(1);
            var user = await db.Users.SingleAsync(u => u.Id == _id);
            user.TwoFactorSecret.Should().NotBe(enabled.Secret);
            user.TwoFactorRecoveryHashes.Should().NotContain(enabled.Codes[0]);
        }
        // Enrollment consumed the current step. The adjacent future step is
        // accepted by the configured drift window and avoids waiting in tests.
        var code = Totp.Code(enabled.Secret, DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30 + 1);
        var response = await Login(code); response.StatusCode.Should().Be(HttpStatusCode.OK);
        var auth = (await response.Content.ReadFromJsonAsync<AuthDTO>())!;
        (await Me(auth.AccessToken)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Login(code)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await Login(enabled.Codes[0], true)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Login(enabled.Codes[0], true)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await Manage(auth.AccessToken, "disable", enabled.Codes[1], true, "wrongpass")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Manage(auth.AccessToken, "disable", enabled.Codes[1], true)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Me(auth.AccessToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await Session()).RequiresTwoFactor.Should().BeFalse();
    }

    [Fact]
    public async Task RepeatedFailuresLockAccountAcrossLoginRequests()
    {
        var enabled = await Enable();
        for (var n = 0; n < 5; n++) (await Login("invalid")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await Login(enabled.Codes[0], true)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await Update(u => u.TwoFactorLockedUntil = DateTime.UtcNow.AddSeconds(-1));
        (await Login(enabled.Codes[0], true)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task SetupNeedsPasswordAndValidUnexpiredConfirmation()
    {
        var auth = await Session();
        (await Manage(auth.AccessToken, "setup", password: "wrongpass")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var setup = (await (await Manage(auth.AccessToken, "setup")).Content.ReadFromJsonAsync<TwoFactorSetupDTO>())!;
        (await Manage(auth.AccessToken, "enable", "invalid")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await Update(u => u.TwoFactorSetupExpiresAt = DateTime.UtcNow.AddSeconds(-1));
        (await Manage(auth.AccessToken, "enable", Totp.Code(setup.Secret, DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30)))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Session()).RequiresTwoFactor.Should().BeFalse();
        (await _client.PostAsJsonAsync("/api/auth/two-factor/setup", new { password = Password }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PasswordChangeKeepsEnrolledFactorAndRecoveryCodes()
    {
        var enabled = await Enable();
        using var scope = _factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<Qontract.Application.Services.IAuthService>();
        (await service.ChangePasswordAsync(_id, Password, "replacement123")).Should().BeTrue();
        var result = await Login(password: "replacement123");
        (await result.Content.ReadFromJsonAsync<AuthDTO>())!.RequiresTwoFactor.Should().BeTrue();
        (await Login(enabled.Codes[0], true, "replacement123")).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
