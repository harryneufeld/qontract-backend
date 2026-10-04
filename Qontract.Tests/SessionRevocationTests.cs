using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Data.Common;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Qontract.Application.Models;
using Qontract.Application.Services;
using Qontract.Core.Security;
using Qontract.Domain.Models;
using Qontract.Infrastructure.Data;

namespace Qontract.Tests;

public class SessionRevocationTests : IClassFixture<QontractWebApplicationFactory>
{
    private const string Password = "password123";
    private const string Hash = "cW9udHJhY3Rfc2VlZF9zYWx0:IqHPhrNNkcLnI0/caOLUw3Cixr9EUYwymWWdYK8DLcA=";
    private readonly QontractWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly string _email = $"session-{Guid.NewGuid():N}@example.com";
    private readonly int _userId;

    public SessionRevocationTests(QontractWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = new User { Name = "Session user", Email = _email, PasswordHash = Hash,
            TenantId = db.Tenants.First().Id, IsActive = true, Role = UserRole.User };
        db.Users.Add(user); db.SaveChanges(); _userId = user.Id;
    }

    private async Task<AuthDTO> Login(string password = Password)
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new { email = _email, password });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<AuthDTO>())!;
    }

    private async Task<HttpResponseMessage> Request(string token, string path = "/api/auth/me", object? body = null)
    {
        using var request = new HttpRequestMessage(body == null ? HttpMethod.Get : HttpMethod.Post, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body != null) request.Content = JsonContent.Create(body);
        return await _client.SendAsync(request);
    }

    [Fact]
    public async Task LogoutRejectsRetainedToken_WhileOtherLoginRemainsActive()
    {
        var first = await Login(); var second = await Login();
        (await Request(first.AccessToken, "/api/auth/logout", new { })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Request(first.AccessToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await Request(first.AccessToken, "/api/auth/logout", new { })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await Request(second.AccessToken)).StatusCode.Should().Be(HttpStatusCode.OK);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await db.AuthSessions.CountAsync(s => s.UserId == _userId && s.RevokedAt != null)).Should().Be(1);
    }

    [Fact]
    public async Task PasswordChangeRejectsAllOldSessions_AndNewPasswordCanLogIn()
    {
        var first = await Login(); var second = await Login();
        (await Request(first.AccessToken, "/api/auth/change-password",
            new { currentPassword = Password, newPassword = "replacement123" })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Request(first.AccessToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await Request(second.AccessToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var next = await Login("replacement123");
        (await Request(next.AccessToken)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await _client.PostAsJsonAsync("/api/auth/login", new { email = _email, password = Password }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await db.Users.SingleAsync(u => u.Id == _userId)).AuthVersion.Should().Be(1);
    }

    [Fact]
    public async Task FailedPasswordChangeKeepsSessionAndCredentialVersion()
    {
        var auth = await Login();
        (await Request(auth.AccessToken, "/api/auth/change-password",
            new { currentPassword = "wrongpass", newPassword = "replacement123" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Request(auth.AccessToken)).StatusCode.Should().Be(HttpStatusCode.OK);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await db.Users.SingleAsync(u => u.Id == _userId)).AuthVersion.Should().Be(0);
    }

    [Fact]
    public async Task TenantSwitchKeepsSession_AndLogoutRejectsBothTenantTokens()
    {
        int tenantId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var tenant = new Tenant { Name = "Second scope", IsActive = true };
            db.Tenants.Add(tenant); await db.SaveChangesAsync(); tenantId = tenant.Id;
            db.TenantMemberships.Add(new TenantMembership { UserId = _userId, TenantId = tenantId,
                Role = UserRole.User, IsActive = true });
            await db.SaveChangesAsync();
        }
        var first = await Login();
        var response = await Request(first.AccessToken, "/api/auth/switch-tenant", new { tenantId });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var switched = (await response.Content.ReadFromJsonAsync<AuthDTO>())!;
        switched.ExpiresAt.Should().Be(first.ExpiresAt);
        switched.ExpiresAt.Kind.Should().Be(DateTimeKind.Utc);
        var reader = new JwtSecurityTokenHandler();
        reader.ReadJwtToken(switched.AccessToken).Claims.Single(c => c.Type == "sid").Value
            .Should().Be(reader.ReadJwtToken(first.AccessToken).Claims.Single(c => c.Type == "sid").Value);
        (await Request(switched.AccessToken, "/api/auth/logout", new { })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Request(first.AccessToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await Request(switched.AccessToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("foreign")]
    [InlineData("version")]
    [InlineData("expired")]
    public async Task RejectsLegacyOrInconsistentSessionClaims(string kind)
    {
        var auth = await Login();
        (await Request(auth.AccessToken)).StatusCode.Should().Be(HttpStatusCode.OK);
        var reader = new JwtSecurityTokenHandler();
        var original = reader.ReadJwtToken(auth.AccessToken);
        var claims = original.Claims.ToList();
        if (kind == "missing") claims.RemoveAll(c => c.Type is "sid" or "auth_version");
        if (kind == "foreign")
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var other = db.Users.First(u => u.Id != _userId);
            var session = new AuthSession { UserId = other.Id, AuthVersion = other.AuthVersion,
                CreatedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddHours(1) };
            db.AuthSessions.Add(session); await db.SaveChangesAsync();
            claims.RemoveAll(c => c.Type == "sid"); claims.Add(new Claim("sid", session.Id.ToString()));
        }
        if (kind == "version")
        {
            claims.RemoveAll(c => c.Type == "auth_version"); claims.Add(new Claim("auth_version", "99"));
        }
        if (kind == "expired")
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var session = await db.AuthSessions.SingleAsync(s => s.UserId == _userId);
            session.ExpiresAt = DateTime.UtcNow.AddMinutes(-1); await db.SaveChangesAsync();
        }
        // Re-sign test tokens to isolate session validation from signature checks.
        var token = new JwtSecurityToken(original.Issuer, "QontractClients", claims,
            expires: DateTime.UtcNow.AddHours(1), signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.ASCII.GetBytes(JwtKeys.DevelopmentFallback)), SecurityAlgorithms.HmacSha256));
        (await Request(reader.WriteToken(token))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentCredentialChangeOrDeactivationCannotBeOverwritten(bool deactivate)
    {
        var auth = await Login();
        using var scope = _factory.Services.CreateScope();
        var connection = scope.ServiceProvider.GetRequiredService<SqliteConnection>();
        var interceptor = new CredentialRace(_userId, deactivate);
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection).AddInterceptors(interceptor).Options);
        var service = new AuthService(db, new ConfigurationBuilder().Build(), NullLogger<AuthService>.Instance);
        (await service.ChangePasswordAsync(_userId, Password, "replacement123")).Should().BeFalse();
        interceptor.Triggered.Should().BeTrue();
        var user = await db.Users.AsNoTracking().SingleAsync(u => u.Id == _userId);
        user.AuthVersion.Should().Be(1);
        user.PasswordHash.Should().Be(deactivate ? Hash : "concurrent replacement");
        user.IsActive.Should().Be(!deactivate);
        (await Request(auth.AccessToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private sealed class CredentialRace(int userId, bool deactivate) : DbCommandInterceptor
    {
        public bool Triggered { get; private set; }
        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!Triggered && command.CommandText.StartsWith("UPDATE") && command.CommandText.Contains("AuthVersion"))
            {
                Triggered = true;
                await using var race = command.Connection!.CreateCommand();
                race.CommandText = deactivate
                    ? "UPDATE Users SET IsActive = 0, AuthVersion = AuthVersion + 1 WHERE Id = @id"
                    : "UPDATE Users SET PasswordHash = 'concurrent replacement', AuthVersion = AuthVersion + 1 WHERE Id = @id";
                var id = race.CreateParameter(); id.ParameterName = "@id"; id.Value = userId; race.Parameters.Add(id);
                await race.ExecuteNonQueryAsync(cancellationToken);
            }
            return result;
        }
    }

    [Fact]
    public async Task AdministratorPasswordReplacementAlsoRevokesUserSessions()
    {
        var auth = await Login();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var tenantId = (await db.Users.SingleAsync(u => u.Id == _userId)).TenantId;
            var users = scope.ServiceProvider.GetRequiredService<IUserService>();
            await users.UpdateUserAsync(_userId, tenantId, new UserUpdateDTO { Password = "replacement123" }, "admin");
        }
        (await Request(auth.AccessToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await Request((await Login("replacement123")).AccessToken)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task StaleTrackedAccountCannotOverwriteConcurrentPasswordChange()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var stale = await db.Users.SingleAsync(u => u.Id == _userId);
        await db.Users.Where(u => u.Id == _userId).ExecuteUpdateAsync(s => s
            .SetProperty(u => u.AuthVersion, u => u.AuthVersion + 1)
            .SetProperty(u => u.PasswordHash, "concurrent replacement"));
        stale.PasswordHash = "stale replacement";
        stale.AuthVersion++;
        var save = async () => await db.SaveChangesAsync();
        await save.Should().ThrowAsync<DbUpdateConcurrencyException>();
        (await db.Users.AsNoTracking().SingleAsync(u => u.Id == _userId))
            .PasswordHash.Should().Be("concurrent replacement");
    }
}
