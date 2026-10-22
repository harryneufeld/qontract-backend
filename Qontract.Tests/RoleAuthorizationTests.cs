using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Xunit;
using Qontract.Application.Models;
using Qontract.Domain.Models;
using Qontract.Infrastructure.Data;

namespace Qontract.Tests;

/// <summary>
/// Role enforcement tests (issue #24): the JWT carries a role claim
/// (tenant_admin/user/contractor via UserRole), and the admin-only
/// endpoints — user management, tenant management, member invitation —
/// must reject everyone except tenant admins with 403. Regular members keep
/// access to the non-management endpoints; the legacy Contractor role has
/// no content access at all (issue #80).
///
/// Tests within this class run sequentially, so mutating the shared
/// client's Authorization header per test is safe.
/// </summary>
public class RoleAuthorizationTests : IClassFixture<QontractWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly QontractWebApplicationFactory _factory;

    // Salted SHA256 of "password123" — same format as the seeded admins in
    // Program.cs, so non-admin accounts can log in with a known password.
    private const string SeedPasswordHash =
        "cW9udHJhY3Rfc2VlZF9zYWx0:IqHPhrNNkcLnI0/caOLUw3Cixr9EUYwymWWdYK8DLcA=";

    public RoleAuthorizationTests(QontractWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        SeedNonAdminUsers();
    }

    /// <summary>
    /// Program.cs only seeds tenant admins. Add a regular member and a
    /// contractor to the shared in-memory database so we can log in as
    /// non-admins. Idempotent: the constructor runs once per test method.
    /// </summary>
    private void SeedNonAdminUsers()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var tenantId = db.Tenants.First(t => t.Name == "Test Family").Id;

        if (!db.Users.Any(u => u.Email == "member@qontract.com"))
        {
            db.Users.Add(new User
            {
                Name = "Regular Member",
                Email = "member@qontract.com",
                PasswordHash = SeedPasswordHash,
                Role = UserRole.User,
                IsActive = true,
                TenantId = tenantId
            });
        }

        if (!db.Users.Any(u => u.Email == "contractor1@qontract.com"))
        {
            db.Users.Add(new User
            {
                Name = "Regular Contractor",
                Email = "contractor1@qontract.com",
                PasswordHash = SeedPasswordHash,
                Role = UserRole.Contractor,
                IsActive = true,
                TenantId = tenantId
            });
        }

        db.SaveChanges();
    }

    private async Task<string> GetTokenAsync(string email)
    {
        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", new { email, password = "password123" });
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var auth = await loginResponse.Content.ReadFromJsonAsync<AuthDTO>();
        auth.Should().NotBeNull();
        return auth!.AccessToken;
    }

    private void Authenticate(string token)
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    [Fact]
    public async Task RegularUser_CannotAccessUserManagementEndpoints()
    {
        Authenticate(await GetTokenAsync("member@qontract.com"));

        var list = await _client.GetAsync("/api/users");
        list.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var update = await _client.PutAsJsonAsync("/api/users/1", new { role = "User" });
        update.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var deactivate = await _client.PatchAsync("/api/users/1/deactivate", null);
        deactivate.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task RegularUser_CannotAccessTenantManagementEndpoints()
    {
        Authenticate(await GetTokenAsync("member@qontract.com"));

        var list = await _client.GetAsync("/api/tenants");
        list.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Any signed-in user may create a workspace and becomes its admin.
        var create = await _client.PostAsJsonAsync("/api/tenants", new { name = "Member Created Tenant", type = "Family" });
        create.StatusCode.Should().Be(HttpStatusCode.Created);

        var update = await _client.PutAsJsonAsync("/api/tenants/1", new { name = "Rogue Tenant" });
        update.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var deactivate = await _client.PatchAsync("/api/tenants/1/deactivate", null);
        deactivate.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Contractor_CannotAccessManagementEndpoints()
    {
        Authenticate(await GetTokenAsync("contractor1@qontract.com"));

        var users = await _client.GetAsync("/api/users");
        users.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var tenants = await _client.GetAsync("/api/tenants");
        tenants.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("/api/contracts")]
    [InlineData("/api/contractors")]
    [InlineData("/api/documents/trash")]
    [InlineData("/api/invoices")]
    public async Task LegacyContractorRole_CannotAccessWorkspaceContent(string route)
    {
        // Only administrators and regular members use the app (issue #80):
        // a membership still carrying the legacy Contractor role gets nothing.
        Authenticate(await GetTokenAsync("contractor1@qontract.com"));

        (await _client.GetAsync(route)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await _client.PostAsJsonAsync("/api/contractors", new { name = "Planted", type = "Company" }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task LegacyContractorRole_CanStillManageOwnSession()
    {
        Authenticate(await GetTokenAsync("contractor1@qontract.com"));

        (await _client.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await _client.PostAsync("/api/auth/logout", null)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task TenantAdmin_CanStillManageUsersAndTenants()
    {
        Authenticate(await GetTokenAsync("admin@qontract.com"));

        var listUsers = await _client.GetAsync("/api/users");
        listUsers.StatusCode.Should().Be(HttpStatusCode.OK);

        // Members join through invitations (issue #80).
        var invite = await _client.PostAsJsonAsync("/api/tenants/1/invitations", new { role = "User" });
        invite.StatusCode.Should().Be(HttpStatusCode.OK);

        var listTenants = await _client.GetAsync("/api/tenants");
        listTenants.StatusCode.Should().Be(HttpStatusCode.OK);

        // Tenant management works for admins.
        var createTenant = await _client.PostAsJsonAsync("/api/tenants", new { name = "Admin Created Tenant", type = "Company" });
        createTenant.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task RegularUser_CanStillAccessNonManagementEndpoints()
    {
        Authenticate(await GetTokenAsync("member@qontract.com"));

        var contractors = await _client.GetAsync("/api/contractors");
        contractors.StatusCode.Should().Be(HttpStatusCode.OK);

        var contracts = await _client.GetAsync("/api/contracts");
        contracts.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Anonymous_CannotAccessManagementEndpoints()
    {
        _client.DefaultRequestHeaders.Authorization = null;

        var users = await _client.GetAsync("/api/users");
        users.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var tenants = await _client.GetAsync("/api/tenants");
        tenants.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
