using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Qontract.API.Services;
using Qontract.Application.Interfaces;
using Qontract.Application.Models;
using Qontract.Application.Services;
using Qontract.Domain.Models;
using Qontract.Infrastructure.Data;
using Qontract.Infrastructure.Email;

namespace Qontract.Tests;

public class PasswordResetTests : IDisposable
{
    private readonly QontractWebApplicationFactory _root = new();
    private readonly Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> _host;
    private readonly HttpClient _client;
    private readonly Mail _mail = new();
    private readonly string _email = $"reset-{Guid.NewGuid():N}@example.com";
    private readonly int _userId;
    private const string Hash = "cW9udHJhY3Rfc2VlZF9zYWx0:IqHPhrNNkcLnI0/caOLUw3Cixr9EUYwymWWdYK8DLcA=";

    public PasswordResetTests()
    {
        _host = _root.WithWebHostBuilder(b =>
        {
            b.UseSetting("PasswordReset:Enabled", "true");
            b.UseSetting("PasswordReset:PublicAppUrl", "https://app.example.test/qontract/");
            b.UseSetting("Smtp:Host", "smtp.invalid"); b.UseSetting("Smtp:Port", "587");
            b.UseSetting("Smtp:From", "qontract@example.test");
            b.ConfigureServices(s =>
            {
                foreach (var worker in s.Where(d => d.ServiceType == typeof(IHostedService)
                    && d.ImplementationType == typeof(PasswordResetWorker)).ToList()) s.Remove(worker);
                s.RemoveAll<IResetEmailSender>(); s.AddSingleton<IResetEmailSender>(_mail);
            });
        });
        _client = _host.CreateClient();
        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = new User { Name = "Reset user", Email = _email, PasswordHash = Hash,
            TenantId = db.Tenants.First().Id, Role = UserRole.User, IsActive = true };
        db.Users.Add(user); db.SaveChanges(); _userId = user.Id;
    }

    public void Dispose() { _client.Dispose(); _host.Dispose(); _root.Dispose(); }
    private async Task Deliver(string? email = null)
    {
        using var scope = _host.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<PasswordResetService>().DeliverAsync(email ?? _email);
    }
    private async Task Update(Action<ApplicationDbContext> action)
    {
        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        action(db); await db.SaveChangesAsync();
    }
    private Task<HttpResponseMessage> Reset(string? token = null, string password = "replacement123") =>
        _client.PostAsJsonAsync("/api/auth/reset-password", new { token = token ?? _mail.Token, newPassword = password });

    [Fact]
    public async Task RequestHasIdenticalAcknowledgement_AndNoEmailOrTokenInResponse()
    {
        var known = await _client.PostAsJsonAsync("/api/auth/forgot-password", new { email = _email });
        var unknown = await _client.PostAsJsonAsync("/api/auth/forgot-password", new { email = "unknown@example.test" });
        known.StatusCode.Should().Be(HttpStatusCode.Accepted); unknown.StatusCode.Should().Be(known.StatusCode);
        (await unknown.Content.ReadAsStringAsync()).Should().Be(await known.Content.ReadAsStringAsync());
        _mail.Sent.Should().Be(0);
        var queue = _host.Services.GetRequiredService<PasswordResetQueue>();
        await using var entries = queue.ReadAllAsync(CancellationToken.None).GetAsyncEnumerator();
        (await entries.MoveNextAsync()).Should().BeTrue(); entries.Current.Should().Be(_email);
    }

    [Fact]
    public async Task ResetStoresOnlyHash_ChangesPasswordOnce_AndRevokesOldSession()
    {
        var login = await _client.PostAsJsonAsync("/api/auth/login", new { email = _email, password = "password123" });
        var auth = (await login.Content.ReadFromJsonAsync<AuthDTO>())!;
        await Deliver();
        _mail.Link!.GetLeftPart(UriPartial.Path).Should().Be("https://app.example.test/qontract/");
        using (var scope = _host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var record = await db.PasswordResets.SingleAsync();
            record.TokenHash.Should().HaveLength(64).And.NotBe(_mail.Token);
            (record.ExpiresAt - record.CreatedAt).Should().Be(TimeSpan.FromMinutes(30));
        }
        var response = await Reset(); response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().NotContain("accessToken");
        (await Reset()).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        (await _client.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _client.DefaultRequestHeaders.Authorization = null;
        (await _client.PostAsJsonAsync("/api/auth/login", new { email = _email, password = "password123" }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await _client.PostAsJsonAsync("/api/auth/login", new { email = _email, password = "replacement123" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("version")]
    [InlineData("email")]
    [InlineData("inactive")]
    public async Task ChangedAccountOrExpiredLinkCannotResetPassword(string change)
    {
        await Deliver();
        await Update(db =>
        {
            var user = db.Users.Single(u => u.Id == _userId);
            if (change == "expired") db.PasswordResets.Single().ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
            if (change == "version") user.AuthVersion++;
            if (change == "email") user.Email = "changed@example.test";
            if (change == "inactive") user.IsActive = false;
        });
        (await Reset()).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var scope = _host.Services.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Users.SingleAsync(u => u.Id == _userId))
            .PasswordHash.Should().Be(Hash);
    }

    [Fact]
    public async Task MigratedUserCannotLoginUntilSetup_AndRetainsMembership()
    {
        await Update(db => db.Users.Single(u => u.Id == _userId).RequiresPasswordSetup = true);
        (await _client.PostAsJsonAsync("/api/auth/login", new { email = _email, password = "password123" }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await Deliver(); (await Reset()).StatusCode.Should().Be(HttpStatusCode.OK);
        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = await db.Users.SingleAsync(u => u.Id == _userId);
        user.RequiresPasswordSetup.Should().BeFalse(); user.AuthVersion.Should().Be(1);
        (await db.TenantMemberships.CountAsync(m => m.UserId == _userId)).Should().Be(1);
    }

    [Fact]
    public async Task CooldownAndHourlyLimitBoundEmails_AndNewLinkSupersedesOld()
    {
        await Deliver(); var first = _mail.Token;
        await Deliver(); _mail.Sent.Should().Be(1);
        for (var i = 0; i < 2; i++)
        {
            await Update(db => { foreach (var r in db.PasswordResets) r.CreatedAt = DateTime.UtcNow.AddMinutes(-2); });
            await Deliver();
        }
        _mail.Sent.Should().Be(3);
        await Update(db => { foreach (var r in db.PasswordResets) r.CreatedAt = DateTime.UtcNow.AddMinutes(-2); });
        await Deliver(); _mail.Sent.Should().Be(3);
        (await Reset(first)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Reset()).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task FailedDeliveryInvalidatesToken_AndUnknownInactiveUsersSendNothing()
    {
        await Deliver("unknown@example.test"); _mail.Sent.Should().Be(0);
        _mail.Fail = true;
        var send = async () => await Deliver(); await send.Should().ThrowAsync<IOException>();
        (await Reset()).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await Update(db => db.Users.Single(u => u.Id == _userId).IsActive = false);
        await Deliver(); _mail.Sent.Should().Be(1);
    }

    [Fact]
    public async Task InvalidTokenOrPasswordCannotMutateAccount()
    {
        await Deliver();
        (await Reset("bad-token")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Reset(password: "short")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Reset(password: new string('x', 256))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Reset()).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("http://app.example.test")]
    [InlineData("https://user:secret@app.example.test")]
    [InlineData("https://app.example.test/?redirect=evil")]
    [InlineData("https://app.example.test/#route")]
    public void RejectsUnsafePublicUrl(string url)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["PasswordReset:PublicAppUrl"] = url }).Build();
        Action validate = () => PasswordResetService.PublicBase(config);
        validate.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void RejectsInsecureOrIncompleteSmtpConfiguration()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["Smtp:Host"] = "smtp.invalid", ["Smtp:Port"] = "587", ["Smtp:From"] = "sender@example.test", ["Smtp:TlsMode"] = "None" }).Build();
        Action validate = () => SmtpResetEmailSender.ValidateConfiguration(config);
        validate.Should().Throw<InvalidOperationException>();
    }

    private sealed class Mail : IResetEmailSender
    {
        public int Sent { get; private set; }
        public Uri? Link { get; private set; }
        public string Token => Link!.Fragment.Split("token=")[1];
        public bool Fail { get; set; }
        public Task SendAsync(string email, Uri resetLink, CancellationToken ct)
        { Sent++; Link = resetLink; if (Fail) throw new IOException("Fake delivery failure"); return Task.CompletedTask; }
    }

    [Fact]
    public async Task AccountEmailChangeAndReversalDoNotReviveOldLink()
    {
        await Deliver();
        using (var scope = _host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var tenant = (await db.Users.SingleAsync(u => u.Id == _userId)).TenantId;
            var users = scope.ServiceProvider.GetRequiredService<IUserService>();
            await users.UpdateUserAsync(_userId, tenant, new UserUpdateDTO { Email = "changed@example.test" }, "admin");
            await users.UpdateUserAsync(_userId, tenant, new UserUpdateDTO { Email = _email }, "admin");
        }
        (await Reset()).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
