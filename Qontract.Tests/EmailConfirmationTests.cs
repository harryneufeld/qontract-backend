using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Qontract.API.Services;
using Qontract.Application.Interfaces;
using Qontract.Application.Models;
using Qontract.Application.Services;
using Qontract.Infrastructure.Data;

namespace Qontract.Tests;

public class EmailConfirmationTests : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly QontractWebApplicationFactory _root = new();
    private readonly Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> _host;
    private readonly HttpClient _client;
    private readonly Mail _mail = new();
    private readonly string _email = $"confirm-{Guid.NewGuid():N}@example.com";
    private const string Password = "confirm-password-123";

    public EmailConfirmationTests()
    {
        _host = _root.WithWebHostBuilder(b =>
        {
            b.UseSetting("Registration:AllowPublic", "true");
            b.UseSetting("Registration:RequireEmailConfirmation", "true");
            b.UseSetting("PasswordReset:Enabled", "true");
            b.UseSetting("PasswordReset:PublicAppUrl", "https://app.example.test/qontract/");
            b.UseSetting("Smtp:Host", "smtp.invalid"); b.UseSetting("Smtp:Port", "587");
            b.UseSetting("Smtp:From", "qontract@example.test");
            b.ConfigureServices(s =>
            {
                foreach (var worker in s.Where(d => d.ServiceType == typeof(IHostedService)
                    && d.ImplementationType == typeof(PasswordResetWorker)).ToList()) s.Remove(worker);
                s.RemoveAll<IConfirmationEmailSender>(); s.AddSingleton<IConfirmationEmailSender>(_mail);
                s.RemoveAll<IResetEmailSender>(); s.AddSingleton<IResetEmailSender>(_mail);
            });
        });
        _client = _host.CreateClient();
    }

    public void Dispose() { _client.Dispose(); _host.Dispose(); _root.Dispose(); }

    private Task<HttpResponseMessage> Register(string? email = null) => _client.PostAsJsonAsync("/api/auth/register",
        new { name = "New user", email = email ?? _email, password = Password, tenantName = "Confirm family" });
    private Task<HttpResponseMessage> Login(string password = Password) =>
        _client.PostAsJsonAsync("/api/auth/login", new { email = _email, password });
    private Task<HttpResponseMessage> Confirm(string? token = null) =>
        _client.PostAsJsonAsync("/api/auth/confirm-email", new { token = token ?? _mail.Token });
    private async Task Deliver(string? email = null)
    {
        using var scope = _host.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<EmailConfirmationService>().DeliverAsync(email ?? _email);
    }
    private async Task Update(Func<ApplicationDbContext, Task> action)
    {
        using var scope = _host.Services.CreateScope();
        await action(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
    }

    [Fact]
    public async Task Registration_IssuesNoSession_AndQueuesConfirmation()
    {
        var policy = await _client.GetFromJsonAsync<JsonElement>("/api/auth/registration");
        policy.GetProperty("requireEmailConfirmation").GetBoolean().Should().BeTrue();

        var response = await Register();
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("confirmationRequired").And.NotContain("accessToken");

        var queue = _host.Services.GetRequiredService<PasswordResetQueue>();
        await using var entries = queue.ReadAllAsync(CancellationToken.None).GetAsyncEnumerator();
        (await entries.MoveNextAsync()).Should().BeTrue();
        entries.Current.Email.Should().Be(_email);
        entries.Current.Kind.Should().Be(AccountEmailKind.EmailConfirmation);
    }

    [Fact]
    public async Task UnconfirmedAccount_CannotSignIn_UntilLinkIsRedeemedOnce()
    {
        (await Register()).StatusCode.Should().Be(HttpStatusCode.Accepted);
        var blocked = await Login();
        blocked.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await blocked.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().Should().Be("email_not_confirmed");

        await Deliver();
        _mail.Sent.Should().Be(1);
        _mail.Link!.AbsoluteUri.Should().StartWith("https://app.example.test/qontract/confirm-email?token=");
        _mail.Link.Fragment.Should().BeEmpty();

        (await Confirm()).StatusCode.Should().Be(HttpStatusCode.OK);
        var login = await Login();
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        (await login.Content.ReadFromJsonAsync<AuthDTO>(JsonOptions))!.AccessToken.Should().NotBeEmpty();
        (await Confirm()).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Code_ConfirmsAndStartsASession_ForAPersonalWorkspaceAccount()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/register", new { email = _email, password = Password });
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        await Deliver();
        _mail.Code.Should().MatchRegex("^[0-9]{6}$");

        var confirmed = await _client.PostAsJsonAsync("/api/auth/confirm-email-code", new { email = _email, code = _mail.Code });
        confirmed.StatusCode.Should().Be(HttpStatusCode.OK);
        var session = (await confirmed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("session");
        session.GetProperty("accessToken").GetString().Should().NotBeEmpty();
        session.GetProperty("user").GetProperty("tenantId").GetInt32().Should().Be(0);
        session.GetProperty("user").GetProperty("name").GetString().Should().NotBeNullOrEmpty();
        session.GetProperty("user").GetProperty("onboardingCompleted").GetBoolean().Should().BeFalse();
        (await _client.PostAsJsonAsync("/api/auth/confirm-email-code", new { email = _email, code = _mail.Code }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);

        _client.DefaultRequestHeaders.Authorization = new("Bearer", session.GetProperty("accessToken").GetString());
        var profile = await _client.PutAsJsonAsync("/api/auth/profile",
            new { name = "Ada", defaultCurrency = "chf", language = "de", onboardingCompleted = true });
        profile.StatusCode.Should().Be(HttpStatusCode.OK);
        var user = await profile.Content.ReadFromJsonAsync<JsonElement>();
        user.GetProperty("name").GetString().Should().Be("Ada");
        user.GetProperty("defaultCurrency").GetString().Should().Be("CHF");
        user.GetProperty("onboardingCompleted").GetBoolean().Should().BeTrue();
        var currency = await _client.GetFromJsonAsync<JsonElement>("/api/contracts/default-currency");
        currency.GetProperty("currency").GetString().Should().Be("CHF");
    }

    [Fact]
    public async Task Code_IsBurnedAfterFiveWrongGuesses_AndExpires()
    {
        (await _client.PostAsJsonAsync("/api/auth/register", new { email = _email, password = Password }))
            .StatusCode.Should().Be(HttpStatusCode.Accepted);
        await Deliver();
        var wrong = _mail.Code == "000000" ? "111111" : "000000";
        for (var i = 0; i < EmailConfirmationService.MaxCodeAttempts; i++)
            (await _client.PostAsJsonAsync("/api/auth/confirm-email-code", new { email = _email, code = wrong }))
                .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await _client.PostAsJsonAsync("/api/auth/confirm-email-code", new { email = _email, code = _mail.Code }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);

        await Update(db => db.EmailConfirmations.ExecuteUpdateAsync(s => s.SetProperty(c => c.CreatedAt, DateTime.UtcNow.AddMinutes(-2))));
        await Deliver();
        await Update(db => db.EmailConfirmations.ExecuteUpdateAsync(s => s.SetProperty(c => c.CodeExpiresAt, DateTime.UtcNow.AddMinutes(-1))));
        (await _client.PostAsJsonAsync("/api/auth/confirm-email-code", new { email = _email, code = _mail.Code }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Login()).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Link_AlsoStartsASession()
    {
        (await Register()).StatusCode.Should().Be(HttpStatusCode.Accepted);
        await Deliver();
        var response = await Confirm();
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("session").GetProperty("accessToken").GetString().Should().NotBeEmpty();
    }

    [Fact]
    public async Task WrongPassword_ForUnconfirmedAccount_DoesNotRevealConfirmationState()
    {
        (await Register()).StatusCode.Should().Be(HttpStatusCode.Accepted);
        var response = await Login("not-the-password");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await response.Content.ReadAsStringAsync()).Should().NotContain("email_not_confirmed");
    }

    [Fact]
    public async Task ExpiredOrMalformedLinks_AreRejected()
    {
        (await Register()).StatusCode.Should().Be(HttpStatusCode.Accepted);
        await Deliver();
        await Update(db => db.EmailConfirmations.ExecuteUpdateAsync(s => s.SetProperty(c => c.ExpiresAt, DateTime.UtcNow.AddMinutes(-1))));
        (await Confirm()).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Confirm("not-a-token")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Login()).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Resend_HasIdenticalAcknowledgement_AndSkipsConfirmedAccounts()
    {
        var known = await _client.PostAsJsonAsync("/api/auth/resend-confirmation", new { email = _email });
        var unknown = await _client.PostAsJsonAsync("/api/auth/resend-confirmation", new { email = "unknown@example.test" });
        known.StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await unknown.Content.ReadAsStringAsync()).Should().Be(await known.Content.ReadAsStringAsync());

        (await Register()).StatusCode.Should().Be(HttpStatusCode.Accepted);
        await Deliver();
        (await Confirm()).StatusCode.Should().Be(HttpStatusCode.OK);
        await Update(db => db.EmailConfirmations.ExecuteDeleteAsync()); // lift the one-per-minute limit
        await Deliver();
        _mail.Sent.Should().Be(1);
    }

    [Fact]
    public async Task Delivery_IsLimited_AndANewLinkReplacesTheOldOne()
    {
        (await Register()).StatusCode.Should().Be(HttpStatusCode.Accepted);
        await Deliver();
        var first = _mail.Token;
        await Deliver();
        _mail.Sent.Should().Be(1); // at most one link per minute
        await Update(db => db.EmailConfirmations.ExecuteUpdateAsync(s => s.SetProperty(c => c.CreatedAt, DateTime.UtcNow.AddMinutes(-2))));
        await Deliver();
        _mail.Sent.Should().Be(2);
        (await Confirm(first)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Confirm()).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task PasswordReset_AlsoConfirmsTheAddress()
    {
        (await Register()).StatusCode.Should().Be(HttpStatusCode.Accepted);
        using (var scope = _host.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<PasswordResetService>().DeliverAsync(_email);
        (await _client.PostAsJsonAsync("/api/auth/reset-password", new { token = _mail.Token, newPassword = "replacement-123" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await Login("replacement-123")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public void RequiredConfirmation_WithoutMailConfiguration_FailsAtStartup()
    {
        using var factory = new QontractWebApplicationFactory().WithWebHostBuilder(b =>
            b.UseSetting("Registration:RequireEmailConfirmation", "true"));
        Assert.ThrowsAny<Exception>(() => factory.CreateClient());
    }

    private sealed class Mail : IConfirmationEmailSender, IResetEmailSender
    {
        public int Sent { get; private set; }
        public Uri? Link { get; private set; }
        public string Token => Link!.Query.Split("token=")[1].Split('&')[0];
        public string? Code { get; private set; }
        public Task SendConfirmationAsync(string email, Uri confirmationLink, string code, CancellationToken ct, string language = "en")
        { Sent++; Link = confirmationLink; Code = code; return Task.CompletedTask; }
        public Task SendAsync(string email, Uri resetLink, CancellationToken ct, string language = "en")
        { Link = resetLink; return Task.CompletedTask; }
    }
}
