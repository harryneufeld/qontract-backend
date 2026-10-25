using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Qontract.Application.Models;
using Qontract.Application.Services;
using Qontract.Domain.Models;
using Qontract.Infrastructure.Data;

namespace Qontract.Tests;

public class RefreshTokenTests : IClassFixture<QontractWebApplicationFactory>
{
    private const string Password = "password123";
    private const string Hash = "cW9udHJhY3Rfc2VlZF9zYWx0:IqHPhrNNkcLnI0/caOLUw3Cixr9EUYwymWWdYK8DLcA=";
    private readonly QontractWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly string _email = $"refresh-{Guid.NewGuid():N}@example.com";
    private readonly int _userId;

    public RefreshTokenTests(QontractWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = new User { Name = "Refresh user", Email = _email, PasswordHash = Hash,
            TenantId = db.Tenants.First().Id, IsActive = true, Role = UserRole.User };
        db.Users.Add(user); db.SaveChanges(); _userId = user.Id;
    }

    private async Task<HttpResponseMessage> LoginResponse(string client, string password = Password, string? device = "Test phone") =>
        await _client.PostAsJsonAsync("/api/auth/login", new { email = _email, password, client, deviceName = device });

    private async Task<AuthDTO> Login(string client = "mobile")
    {
        var response = await LoginResponse(client);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<AuthDTO>())!;
    }

    private async Task<HttpResponseMessage> Refresh(string? token, string? cookie = null, bool header = true)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh") { Content = JsonContent.Create(new { refreshToken = token }) };
        if (header) request.Headers.Add("X-Qontract-Client", "mobile");
        if (cookie != null) request.Headers.Add("Cookie", $"qontract_rt={cookie}");
        return await _client.SendAsync(request);
    }

    private async Task<HttpResponseMessage> Authed(string token, string path = "/api/auth/me", HttpMethod? method = null, object? body = null)
    {
        var request = new HttpRequestMessage(method ?? (body == null ? HttpMethod.Get : HttpMethod.Post), path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body != null) request.Content = JsonContent.Create(body);
        return await _client.SendAsync(request);
    }

    private async Task WithDb(Func<ApplicationDbContext, Task> action)
    {
        using var scope = _factory.Services.CreateScope();
        await action(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
    }

    private static string? CookieValue(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.FirstOrDefault(v => v.StartsWith(name + "="))
            : null;

    [Fact]
    public async Task MobileLogin_ReturnsRefreshToken_AndShortAccessToken()
    {
        var auth = await Login();
        auth.RefreshToken.Should().HaveLength(64);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(auth.AccessToken);
        jwt.ValidTo.Should().BeCloseTo(DateTime.UtcNow.AddMinutes(15), TimeSpan.FromSeconds(30));
        auth.AccessTokenExpiresAt.Should().BeCloseTo(jwt.ValidTo, TimeSpan.FromSeconds(2));
        auth.ExpiresAt.Should().BeCloseTo(DateTime.UtcNow.AddDays(365), TimeSpan.FromMinutes(1));
        await WithDb(async db =>
        {
            var session = await db.AuthSessions.SingleAsync(s => s.UserId == _userId);
            session.RefreshTokenHash.Should().NotBe(auth.RefreshToken).And.HaveLength(64);
            session.ClientType.Should().Be("mobile");
            session.DeviceName.Should().Be("Test phone");
        });
    }

    [Fact]
    public async Task WebLogin_SetsHttpOnlyCookie_AndNoRefreshTokenInBody()
    {
        var response = await LoginResponse("web");
        var body = (await response.Content.ReadFromJsonAsync<AuthDTO>())!;
        body.RefreshToken.Should().BeNull();
        body.ExpiresAt.Should().BeCloseTo(DateTime.UtcNow.AddDays(90), TimeSpan.FromMinutes(1));
        var cookie = CookieValue(response, "qontract_rt");
        cookie.Should().NotBeNull();
        var flags = cookie!.ToLowerInvariant();
        flags.Should().Contain("httponly").And.Contain("secure").And.Contain("samesite=strict").And.Contain("path=/api/auth");
        cookie.Split(';')[0].Should().HaveLength("qontract_rt=".Length + 64);
    }

    [Fact]
    public async Task ClientWithoutIdentity_KeepsPlain24HourSession()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new { email = _email, password = Password });
        var auth = (await response.Content.ReadFromJsonAsync<AuthDTO>())!;
        auth.RefreshToken.Should().BeNull();
        CookieValue(response, "qontract_rt").Should().BeNull();
        new JwtSecurityTokenHandler().ReadJwtToken(auth.AccessToken).ValidTo
            .Should().BeCloseTo(DateTime.UtcNow.AddHours(24), TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task Refresh_RotatesToken_AndIssuesWorkingAccessToken()
    {
        var auth = await Login();
        var response = await Refresh(auth.RefreshToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var next = (await response.Content.ReadFromJsonAsync<AuthDTO>())!;
        next.RefreshToken.Should().HaveLength(64).And.NotBe(auth.RefreshToken);
        (await Authed(next.AccessToken)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Refresh(next.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Refresh_WithoutClientHeader_IsRejected()
    {
        var auth = await Login();
        (await Refresh(auth.RefreshToken, header: false)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Refresh_UnknownToken_Is401()
    {
        (await Refresh(new string('a', 64))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await Refresh(null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_ViaCookie_RotatesCookie()
    {
        var login = await LoginResponse("web");
        var cookie = CookieValue(login, "qontract_rt")!.Split(';')[0]["qontract_rt=".Length..];
        var response = await Refresh(null, cookie);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<AuthDTO>())!.RefreshToken.Should().BeNull();
        var rotated = CookieValue(response, "qontract_rt")!;
        rotated.Split(';')[0].Should().NotBe($"qontract_rt={cookie}");
        rotated.ToLowerInvariant().Should().Contain("httponly");
    }

    [Fact]
    public async Task OldToken_WorksInGraceWindow_ButOnlyYieldsAccessToken()
    {
        var auth = await Login();
        (await Refresh(auth.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.OK);
        var again = await Refresh(auth.RefreshToken);
        again.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = (await again.Content.ReadFromJsonAsync<AuthDTO>())!;
        body.RefreshToken.Should().BeNull();
        (await Authed(body.AccessToken)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task OldToken_AfterGraceWindow_RevokesSession()
    {
        var auth = await Login();
        var rotated = (await (await Refresh(auth.RefreshToken)).Content.ReadFromJsonAsync<AuthDTO>())!;
        await WithDb(db => db.AuthSessions.Where(s => s.UserId == _userId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RotatedAt, DateTime.UtcNow.AddMinutes(-5))));
        (await Refresh(auth.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        // The thief's reuse also ends the legitimate holder's session.
        (await Refresh(rotated.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await Authed(rotated.AccessToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task IdleExpiry_EndsSession()
    {
        var auth = await Login();
        await WithDb(db => db.AuthSessions.Where(s => s.UserId == _userId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.IdleExpiresAt, DateTime.UtcNow.AddMinutes(-1))));
        (await Refresh(auth.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AbsoluteExpiry_EndsSession()
    {
        var auth = await Login();
        await WithDb(db => db.AuthSessions.Where(s => s.UserId == _userId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ExpiresAt, DateTime.UtcNow.AddMinutes(-1))));
        (await Refresh(auth.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_MovesIdleExpiryForward_ButNeverPastAbsoluteExpiry()
    {
        var auth = await Login();
        await WithDb(db => db.AuthSessions.Where(s => s.UserId == _userId).ExecuteUpdateAsync(s =>
            s.SetProperty(x => x.IdleExpiresAt, DateTime.UtcNow.AddDays(1)).SetProperty(x => x.ExpiresAt, DateTime.UtcNow.AddDays(10))));
        (await Refresh(auth.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.OK);
        await WithDb(async db =>
        {
            var session = await db.AuthSessions.SingleAsync(s => s.UserId == _userId);
            session.IdleExpiresAt.Should().BeCloseTo(session.ExpiresAt, TimeSpan.FromSeconds(1));
        });
    }

    [Fact]
    public async Task PasswordChange_KillsRefreshToken()
    {
        var auth = await Login();
        (await Authed(auth.AccessToken, "/api/auth/change-password",
            body: new { currentPassword = Password, newPassword = "replacement123" })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Refresh(auth.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_KillsRefreshToken_AndClearsCookie()
    {
        var login = await LoginResponse("web");
        var auth = (await login.Content.ReadFromJsonAsync<AuthDTO>())!;
        var cookie = CookieValue(login, "qontract_rt")!.Split(';')[0]["qontract_rt=".Length..];
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        request.Headers.Add("Cookie", $"qontract_rt={cookie}");
        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        CookieValue(response, "qontract_rt")!.ToLowerInvariant().Should().Contain("expires=");
        (await Refresh(null, cookie)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task TenantSwitch_SurvivesRefresh()
    {
        int tenantId;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var tenant = new Tenant { Name = "Second scope", IsActive = true };
            db.Tenants.Add(tenant); await db.SaveChangesAsync(); tenantId = tenant.Id;
            db.TenantMemberships.Add(new TenantMembership { UserId = _userId, TenantId = tenantId, Role = UserRole.User, IsActive = true });
            await db.SaveChangesAsync();
        }
        var auth = await Login();
        var switched = await Authed(auth.AccessToken, "/api/auth/switch-tenant", body: new { tenantId });
        switched.StatusCode.Should().Be(HttpStatusCode.OK);
        (await switched.Content.ReadFromJsonAsync<AuthDTO>())!.RefreshToken.Should().BeNull();
        var refreshed = (await (await Refresh(auth.RefreshToken)).Content.ReadFromJsonAsync<AuthDTO>())!;
        refreshed.User!.TenantId.Should().Be(tenantId);
        new JwtSecurityTokenHandler().ReadJwtToken(refreshed.AccessToken).Claims.Single(c => c.Type == "TenantId").Value
            .Should().Be(tenantId.ToString());

        // A removed membership falls back to the personal scope.
        await WithDb(db => db.TenantMemberships.Where(m => m.UserId == _userId && m.TenantId == tenantId)
            .ExecuteUpdateAsync(m => m.SetProperty(x => x.IsActive, false)));
        var fallback = (await (await Refresh(refreshed.RefreshToken)).Content.ReadFromJsonAsync<AuthDTO>())!;
        fallback.User!.TenantId.Should().Be(0);
    }

    [Fact]
    public async Task Sessions_ListMarksCurrent_AndRevokeEndsRefresh()
    {
        var first = await Login(); var second = await Login();
        var list = await (await Authed(first.AccessToken, "/api/auth/sessions")).Content.ReadFromJsonAsync<List<SessionInfoDTO>>();
        list.Should().HaveCount(2);
        list!.Count(s => s.Current).Should().Be(1);
        list.Should().OnlyContain(s => s.DeviceName == "Test phone" && s.ClientType == "mobile");
        var other = list.Single(s => !s.Current);

        (await Authed(first.AccessToken, $"/api/auth/sessions/{other.Id}", HttpMethod.Delete)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await Refresh(second.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await Authed(second.AccessToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await Authed(first.AccessToken)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task RevokeOthers_KeepsOnlyCurrentSession()
    {
        var first = await Login(); var second = await Login(); var third = await Login();
        (await Authed(second.AccessToken, "/api/auth/sessions/revoke-others", body: new { })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Authed(second.AccessToken)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Refresh(first.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await Refresh(third.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AnotherUsersSession_CannotBeRevoked()
    {
        var victim = await Login();
        var otherEmail = $"other-{Guid.NewGuid():N}@example.com";
        await WithDb(async db =>
        {
            db.Users.Add(new User { Name = "Other", Email = otherEmail, PasswordHash = Hash,
                TenantId = db.Tenants.First().Id, IsActive = true, Role = UserRole.User });
            await db.SaveChangesAsync();
        });
        var other = (await (await _client.PostAsJsonAsync("/api/auth/login", new { email = otherEmail, password = Password, client = "mobile" }))
            .Content.ReadFromJsonAsync<AuthDTO>())!;
        var victimSession = Guid.Parse(new JwtSecurityTokenHandler().ReadJwtToken(victim.AccessToken).Claims.Single(c => c.Type == "sid").Value);

        (await Authed(other.AccessToken, $"/api/auth/sessions/{victimSession}", HttpMethod.Delete)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Authed(other.AccessToken, "/api/auth/sessions/revoke-others", body: new { })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Refresh(victim.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.OK);
        var theirs = await (await Authed(other.AccessToken, "/api/auth/sessions")).Content.ReadFromJsonAsync<List<SessionInfoDTO>>();
        theirs.Should().ContainSingle().Which.Id.Should().NotBe(victimSession);
    }

    [Fact]
    public async Task Cleanup_DeletesOnlySessionsEndedLongAgo()
    {
        await Login(); await Login(); await Login();
        await WithDb(async db =>
        {
            var sessions = await db.AuthSessions.Where(s => s.UserId == _userId).ToListAsync();
            sessions[0].RevokedAt = DateTime.UtcNow.AddDays(-31);
            sessions[1].RevokedAt = DateTime.UtcNow.AddDays(-1);
            await db.SaveChangesAsync();
            var cleanup = new AuthSessionCleanup(db, Options.Create(new AuthSessionOptions()), NullLogger<AuthSessionCleanup>.Instance);
            (await cleanup.PurgeAsync()).Should().BeGreaterThanOrEqualTo(1);
            (await db.AuthSessions.CountAsync(s => s.UserId == _userId)).Should().Be(2);
        });
    }
}
