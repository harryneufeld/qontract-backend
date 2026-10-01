using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Qontract.Application.Models;
using Qontract.Core.Security;
using Qontract.Infrastructure.Data;
using Xunit;

namespace Qontract.Tests;

public class MembershipFlowTests : IClassFixture<QontractWebApplicationFactory>
{
    private readonly QontractWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private static readonly JsonSerializerOptions JsonOptions = new()
    { PropertyNameCaseInsensitive = true, Converters = { new JsonStringEnumConverter() } };

    public MembershipFlowTests(QontractWebApplicationFactory factory)
    { _factory = factory; _client = factory.CreateClient(); }

    private async Task<AuthDTO> Login(string email = "admin@qontract.com", string password = "password123")
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new { email, password });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var auth = (await response.Content.ReadFromJsonAsync<AuthDTO>(JsonOptions))!;
        UseToken(auth.AccessToken);
        return auth;
    }
    private void UseToken(string token) => _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    private async Task<TenantDTO> NewTenant()
    {
        var response = await _client.PostAsJsonAsync("/api/tenants", new { name = $"Flow-{Guid.NewGuid():N}", type = "Family" });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<TenantDTO>(JsonOptions))!;
    }
    private async Task<InvitationDTO> Invite(int tenantId, string? email = null, string role = "User")
    {
        var response = await _client.PostAsJsonAsync($"/api/tenants/{tenantId}/invitations", new { email, role });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<InvitationDTO>(JsonOptions))!;
    }

    [Fact]
    public async Task PublicRegistration_IsDisabledByDefault_WithoutSideEffects()
    {
        var policy = await _client.GetFromJsonAsync<JsonElement>("/api/auth/registration");
        policy.GetProperty("allowPublic").GetBoolean().Should().BeFalse();
        var email = $"disabled-{Guid.NewGuid():N}@example.com";
        var response = await _client.PostAsJsonAsync("/api/auth/register",
            new { name = "New user", email, password = "new-password-123", tenantName = "My family" });
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        using var scope = _factory.Services.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Users.AnyAsync(u => u.Email == email)).Should().BeFalse();
    }

    [Fact]
    public async Task EnabledRegistration_CreatesAccountTenantAndAdminMembership_WithCaseInsensitiveIdentity()
    {
        using var enabledFactory = _factory.WithWebHostBuilder(builder => builder.UseSetting("Registration:AllowPublic", "true"));
        var client = enabledFactory.CreateClient();
        var email = $"signup-{Guid.NewGuid():N}@example.com";
        var payload = new { name = "New user", email, password = "pässword-for-registration", tenantName = "Signup family" };
        var response = await client.PostAsJsonAsync("/api/auth/register", payload);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var auth = (await response.Content.ReadFromJsonAsync<AuthDTO>(JsonOptions))!;
        auth.User!.Role.Should().Be("TenantAdmin");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        (await client.GetAsync("/api/contracts")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PostAsJsonAsync("/api/auth/register", payload)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email = email.ToUpperInvariant(), payload.password });
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        using var scope = enabledFactory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await db.Users.CountAsync(u => u.NormalizedEmail == email.ToUpperInvariant())).Should().Be(1);
        var user = await db.Users.SingleAsync(u => u.Id == auth.UserId);
        Passwords.Verify(payload.password, user.PasswordHash).Should().BeTrue();
    }

    [Fact]
    public async Task InvitedSignup_WorksWithPublicRegistrationDisabled_AndNeverExposesCodeHash()
    {
        await Login();
        var tenant = await NewTenant();
        var email = $"invite-{Guid.NewGuid():N}@example.com";
        var invitation = await Invite(tenant.Id, email);
        var list = await _client.GetAsync($"/api/tenants/{tenant.Id}/invitations");
        var listBody = await list.Content.ReadAsStringAsync();
        listBody.Should().NotContain(invitation.Code!);
        listBody.Should().NotContain("codeHash");
        var signup = new { name = "Invited", email, password = "invitation-password", invitationCode = invitation.Code };
        var response = await _client.PostAsJsonAsync("/api/auth/register", signup);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var auth = (await response.Content.ReadFromJsonAsync<AuthDTO>(JsonOptions))!;
        auth.User!.TenantId.Should().Be(tenant.Id);
        auth.User.Role.Should().Be("User");
        UseToken(auth.AccessToken);
        (await _client.GetAsync($"/api/tenants/{tenant.Id}/members")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var replay = await _client.PostAsJsonAsync("/api/auth/register", new
        { name = "Replay", email = $"replay-{Guid.NewGuid():N}@example.com", password = "another-password", invitationCode = invitation.Code });
        replay.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ExistingUser_JoinsSwitchesAndLeaves_WithoutLosingOtherMemberships()
    {
        await Login();
        var tenant = await NewTenant();
        var invitation = await Invite(tenant.Id, "admin2@qontract.com");
        var second = await Login("admin2@qontract.com");
        var originalTenant = second.User!.TenantId;
        var joined = await _client.PostAsJsonAsync("/api/auth/invitations/redeem", new { code = invitation.Code });
        joined.StatusCode.Should().Be(HttpStatusCode.OK);
        var auth = (await joined.Content.ReadFromJsonAsync<AuthDTO>(JsonOptions))!;
        auth.User!.TenantId.Should().Be(tenant.Id);
        auth.User.Role.Should().Be("User");
        UseToken(auth.AccessToken);
        (await _client.GetFromJsonAsync<AuthUserDTO>("/api/auth/me"))!.TenantId.Should().Be(tenant.Id);
        var memberships = await _client.GetFromJsonAsync<List<MembershipDTO>>("/api/auth/memberships", JsonOptions);
        memberships!.Select(m => m.TenantId).Should().Contain(new[] { originalTenant, tenant.Id });
        (await _client.GetAsync("/api/users")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await _client.PostAsJsonAsync("/api/auth/switch-tenant", new { tenantId = int.MaxValue })).StatusCode.Should().Be(HttpStatusCode.NotFound);

        await Login();
        (await _client.DeleteAsync($"/api/tenants/{tenant.Id}/members/{auth.UserId}")).StatusCode.Should().Be(HttpStatusCode.OK);
        UseToken(auth.AccessToken);
        (await _client.GetAsync("/api/contracts")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        // Password login chooses the remaining membership; the account remains usable.
        (await Login("admin2@qontract.com")).User!.TenantId.Should().Be(originalTenant);
        (await _client.PostAsJsonAsync("/api/auth/switch-tenant", new { tenantId = tenant.Id })).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("revoked")]
    [InlineData("wrong-email")]
    public async Task UnavailableInvitation_DoesNotCreateAccountOrMembership(string reason)
    {
        await Login();
        var tenant = await NewTenant();
        var invitation = await Invite(tenant.Id, reason == "wrong-email" ? "someone-else@example.com" : null);
        if (reason == "revoked")
            (await _client.DeleteAsync($"/api/tenants/{tenant.Id}/invitations/{invitation.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        if (reason == "expired")
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            (await db.TenantInvitations.FindAsync(invitation.Id))!.ExpiresAt = DateTime.UtcNow.AddDays(-1);
            await db.SaveChangesAsync();
        }
        var email = $"invalid-{Guid.NewGuid():N}@example.com";
        (await _client.PostAsJsonAsync("/api/auth/register", new
        { name = "Invalid", email, password = "invalid-invitation-password", invitationCode = invitation.Code })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var check = _factory.Services.CreateScope();
        (await check.ServiceProvider.GetRequiredService<ApplicationDbContext>().Users.AnyAsync(u => u.Email == email)).Should().BeFalse();
    }

    [Fact]
    public async Task Demotion_ChangesAuthorizationForAnAlreadyIssuedAdminToken()
    {
        await Login();
        var tenant = await NewTenant();
        var invitation = await Invite(tenant.Id, "admin2@qontract.com", "TenantAdmin");
        await Login("admin2@qontract.com");
        var joined = await _client.PostAsJsonAsync("/api/auth/invitations/redeem", new { code = invitation.Code });
        var second = (await joined.Content.ReadFromJsonAsync<AuthDTO>(JsonOptions))!;
        UseToken(second.AccessToken);
        (await _client.GetAsync($"/api/tenants/{tenant.Id}/members")).StatusCode.Should().Be(HttpStatusCode.OK);

        await Login();
        (await _client.PatchAsync($"/api/tenants/{tenant.Id}/members/{second.UserId}",
            JsonContent.Create(new { role = "User" }))).StatusCode.Should().Be(HttpStatusCode.OK);
        UseToken(second.AccessToken);
        (await _client.GetAsync($"/api/tenants/{tenant.Id}/members")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await _client.GetFromJsonAsync<AuthUserDTO>("/api/auth/me"))!.Role.Should().Be("User");
        (await _client.GetAsync("/api/contracts")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task FinalAdminCannotBeRemovedOrDemoted_AndForeignAdminsCannotManageMemberships()
    {
        var auth = await Login();
        var tenant = await NewTenant();
        (await _client.DeleteAsync($"/api/tenants/{tenant.Id}/members/{auth.UserId}")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await _client.PatchAsync($"/api/tenants/{tenant.Id}/members/{auth.UserId}", JsonContent.Create(new { role = "User" })))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await Login("admin2@qontract.com");
        (await _client.GetAsync($"/api/tenants/{tenant.Id}/members")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _client.PostAsJsonAsync($"/api/tenants/{tenant.Id}/invitations", new { role = "TenantAdmin" }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
