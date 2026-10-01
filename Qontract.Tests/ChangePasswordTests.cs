using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Xunit;
using Qontract.Domain.Models;
using Qontract.Infrastructure.Data;

namespace Qontract.Tests;

/// <summary>
/// Self-service password change (app#11): POST /api/auth/change-password.
///
/// Uses a throwaway user created directly in the shared in-memory database
/// (never the seeded admin — that account is shared with other test
/// classes) so the test is hermetic: mutating the throwaway user's hash
/// cannot leak into other suites.
/// </summary>
public class ChangePasswordTests : IClassFixture<QontractWebApplicationFactory>
{
    private const string Email = "pw-change-user@qontract.com";
    private const string CurrentPassword = "password123";
    private const string NewPassword = "newpass123";
    private const string ThirdPassword = "thirdpass99";

    // Salted SHA256 of "password123" — same seeded hash format as Program.cs.
    private const string SeedPasswordHash =
        "cW9udHJhY3Rfc2VlZF9zYWx0:IqHPhrNNkcLnI0/caOLUw3Cixr9EUYwymWWdYK8DLcA=";

    private readonly HttpClient _client;
    private readonly QontractWebApplicationFactory _factory;
    private readonly int _userId;

    public ChangePasswordTests(QontractWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        _userId = SeedThrowawayUser();
    }

    private int SeedThrowawayUser()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tenantId = db.Tenants.First(t => t.Name == "Test Family").Id;

        var existing = db.Users.FirstOrDefault(u => u.Email == Email);
        if (existing is not null)
        {
            // Reset state so reruns/interference cannot break the flow.
            existing.PasswordHash = SeedPasswordHash;
            existing.IsActive = true;
            db.SaveChanges();
            return existing.Id;
        }

        var user = new User
        {
            Name = "Pw Change User",
            Email = Email,
            PasswordHash = SeedPasswordHash,
            Role = UserRole.User,
            IsActive = true,
            TenantId = tenantId
        };
        db.Users.Add(user);
        db.SaveChanges();
        return user.Id;
    }

    private async Task<string> LoginAsync(string email, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new { email, password });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var auth = await response.Content.ReadFromJsonAsync<Qontract.Application.Models.AuthDTO>();
        return auth!.AccessToken;
    }

    private async Task<HttpResponseMessage> ChangeAsync(string? token, string currentPassword, string newPassword)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/change-password")
        {
            Content = JsonContent.Create(new { currentPassword, newPassword })
        };
        if (token != null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        return await _client.SendAsync(request);
    }

    [Fact]
    public async Task ChangePassword_Unauthenticated_Returns401()
    {
        var response = await ChangeAsync(token: null, CurrentPassword, NewPassword);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ChangePassword_EmptyCurrentPassword_Returns400()
    {
        var token = await LoginAsync(Email, CurrentPassword);
        var response = await ChangeAsync(token, "", NewPassword);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ChangePassword_NewPasswordTooShort_Returns400()
    {
        var token = await LoginAsync(Email, CurrentPassword);
        var response = await ChangeAsync(token, CurrentPassword, "abc");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ChangePassword_WrongCurrentPassword_Returns400AndKeepsOldPassword()
    {
        var token = await LoginAsync(Email, CurrentPassword);
        var response = await ChangeAsync(token, "definitely-wrong", NewPassword);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // The old password must still work — the hash was not replaced.
        await LoginAsync(Email, CurrentPassword);
    }

    [Fact]
    public async Task ChangePassword_ValidInput_SucceedsAndOldPasswordStopsWorking()
    {
        var token = await LoginAsync(Email, CurrentPassword);
        var response = await ChangeAsync(token, CurrentPassword, NewPassword);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // New password works…
        var freshToken = await LoginAsync(Email, NewPassword);
        freshToken.Should().NotBeNullOrEmpty();

        // …and the old one no longer does.
        var oldLogin = await _client.PostAsJsonAsync("/api/auth/login", new { email = Email, password = CurrentPassword });
        oldLogin.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // Restore the seeded password so parallel test classes sharing this
        // user (and the next run of this test) start from a known state.
        var restoreToken = await LoginAsync(Email, NewPassword);
        await ChangeAsync(restoreToken, NewPassword, CurrentPassword);
    }

    [Fact]
    public async Task ChangePassword_ValidInput_StoresNewHashNotPlaintext()
    {
        var token = await LoginAsync(Email, CurrentPassword);
        var response = await ChangeAsync(token, CurrentPassword, ThirdPassword);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = db.Users.Single(u => u.Id == _userId);

        user.PasswordHash.Should().NotBe(ThirdPassword);
        user.PasswordHash.Should().NotBe(SeedPasswordHash);
        user.PasswordHash.Should().StartWith("pbkdf2-sha512:");
        Qontract.Core.Security.Passwords.Verify(ThirdPassword, user.PasswordHash).Should().BeTrue();

        // Restore the seeded password.
        var restoreToken = await LoginAsync(Email, ThirdPassword);
        await ChangeAsync(restoreToken, ThirdPassword, CurrentPassword);
    }
}
