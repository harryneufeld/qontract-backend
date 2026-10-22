using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Qontract.Application.Models;
using Qontract.Core.Security;
using Qontract.Domain.Models;
using Qontract.Infrastructure.Data;
using Xunit;

namespace Qontract.Tests;

public class PersonalWorkspaceTests(QontractWebApplicationFactory factory) : IClassFixture<QontractWebApplicationFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
        { Converters = { new JsonStringEnumConverter() } };

    private async Task<(HttpClient Client, AuthDTO Auth)> Personal()
    {
        var email = $"personal-{Guid.NewGuid():N}@example.test";
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Users.Add(new User { Name = "Private owner", Email = email, PasswordHash = Passwords.Hash("private-password123"),
                IsActive = true, Role = UserRole.TenantAdmin });
            await db.SaveChangesAsync();
        }
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "private-password123" });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var auth = (await response.Content.ReadFromJsonAsync<AuthDTO>(Json))!;
        auth.User!.TenantId.Should().Be(0);
        auth.User.Role.Should().Be("User");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        return (client, auth);
    }

    private static async Task<ContractorDTO> Contractor(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/contractors", new { name = "Private supplier", type = "Company", tenantId = 1, ownerUserId = 1 });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<ContractorDTO>(Json))!;
    }

    private static async Task<ContractDTO> Contract(HttpClient client, int? contractorId = null)
    {
        var response = await client.PostAsJsonAsync("/api/contracts", new { title = "Private contract", status = "Active", contractorId, tenantId = 1, ownerUserId = 1 });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<ContractDTO>(Json))!;
    }

    [Fact]
    public async Task AccountWithoutMembership_CanUsePrivateWorkspace_WithoutTenantPrivileges()
    {
        var (client, auth) = await Personal();
        (await client.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetFromJsonAsync<List<MembershipDTO>>("/api/auth/memberships", Json))!.Should().BeEmpty();
        foreach (var route in new[] { "/api/tenants", "/api/users", "/api/invoices" })
            (await client.GetAsync(route)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.PostAsJsonAsync("/api/auth/switch-tenant", new { tenantId = 1 })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await db.Users.FindAsync(auth.UserId))!.TenantId.Should().BeNull();
        (await db.TenantMemberships.AnyAsync(m => m.UserId == auth.UserId)).Should().BeFalse();
    }

    [Fact]
    public async Task AccountWithoutMembership_CanCreateWorkspace_AndBecomesItsAdmin()
    {
        var (client, _) = await Personal();
        var create = await client.PostAsJsonAsync("/api/tenants", new { name = "My Family", type = "Family" });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var tenant = (await create.Content.ReadFromJsonAsync<TenantDTO>(Json))!;

        var memberships = await client.GetFromJsonAsync<List<MembershipDTO>>("/api/auth/memberships", Json);
        memberships!.Should().ContainSingle(m => m.TenantId == tenant.Id && m.Role == UserRole.TenantAdmin);
        (await client.PostAsJsonAsync("/api/auth/switch-tenant", new { tenantId = tenant.Id })).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task PrivateCrud_ContractCountsAndStats_AreIsolatedBetweenOwners()
    {
        var (owner, auth) = await Personal();
        var (other, _) = await Personal();
        var contractor = await Contractor(owner);
        contractor.TenantId.Should().BeNull();
        contractor.OwnerUserId.Should().Be(auth.UserId);
        var contract = await Contract(owner, contractor.Id);
        contract.TenantId.Should().BeNull();
        contract.OwnerUserId.Should().Be(auth.UserId);
        (await other.GetFromJsonAsync<List<ContractDTO>>("/api/contracts", Json))!.Should().BeEmpty();
        (await other.GetFromJsonAsync<List<ContractorDTO>>("/api/contractors", Json))!.Should().BeEmpty();
        foreach (var route in new[] { $"/api/contracts/{contract.Id}", $"/api/contractors/{contractor.Id}" })
        {
            (await other.GetAsync(route)).StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await other.PutAsJsonAsync(route, new { title = "Changed", name = "Changed" })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
        (await owner.GetFromJsonAsync<ContractStatsDTO>("/api/contracts/stats", Json))!.Total.Should().Be(1);
        (await other.GetFromJsonAsync<ContractStatsDTO>("/api/contracts/stats", Json))!.Total.Should().Be(0);
        (await owner.GetFromJsonAsync<ContractorDTO>($"/api/contractors/{contractor.Id}", Json))!.ContractCount.Should().Be(1);
        (await owner.PutAsJsonAsync($"/api/contracts/{contract.Id}", new { title = "Updated private contract" })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await other.DeleteAsync($"/api/contracts/{contract.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await owner.DeleteAsync($"/api/contracts/{contract.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ContractorReferences_CannotCrossOwnersOrSharedWorkspace()
    {
        var (owner, _) = await Personal();
        var (other, _) = await Personal();
        var contractor = await Contractor(owner);
        var contract = await Contract(other);
        (await other.PostAsJsonAsync("/api/contracts", new { title = "Forbidden", status = "Active", contractorId = contractor.Id })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await other.PutAsJsonAsync($"/api/contracts/{contract.Id}", new { contractorId = contractor.Id })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var admin = factory.CreateClient();
        var login = await admin.PostAsJsonAsync("/api/auth/login", new { email = "admin@qontract.com", password = "password123" });
        var auth = (await login.Content.ReadFromJsonAsync<AuthDTO>(Json))!;
        admin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        (await admin.GetAsync($"/api/contractors/{contractor.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await admin.PostAsJsonAsync("/api/contracts", new { title = "Forbidden shared", status = "Active", contractorId = contractor.Id })).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PrivateDocuments_AndAddresses_AreAccessibleOnlyToTheirOwner()
    {
        var (owner, _) = await Personal();
        var (other, _) = await Personal();
        var contractor = await Contractor(owner);
        var contract = await Contract(owner, contractor.Id);
        var second = await Contract(owner);
        var response = await owner.PostAsJsonAsync($"/api/documents/contract/{contract.Id}/documents", new
            { fileName = "private.txt", fileType = "text/plain", fileData = Convert.ToBase64String("private evidence"u8.ToArray()) });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var document = (await response.Content.ReadFromJsonAsync<DocumentDTO>(Json))!;
        document.Scope.Should().Be("Personal");
        (await other.GetAsync($"/api/documents/{document.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await other.GetAsync($"/api/documents/{document.Id}/download")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await other.GetAsync($"/api/documents/{document.Id}/versions")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await other.DeleteAsync($"/api/documents/{document.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await other.GetAsync($"/api/documents/contract/{contract.Id}/documents")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await owner.GetFromJsonAsync<List<DocumentDTO>>($"/api/documents/contract/{contract.Id}/documents", Json))!.Should().ContainSingle();
        var link = await owner.PostAsync($"/api/documents/{document.Id}/contracts/{second.Id}", null);
        link.StatusCode.Should().Be(HttpStatusCode.OK);
        (await owner.GetFromJsonAsync<List<DocumentDTO>>($"/api/documents/contract/{second.Id}/documents", Json))!.Should().ContainSingle();
        var replacement = await owner.PostAsJsonAsync($"/api/documents/{document.Id}/reupload", new
            { fileName = "private-v2.txt", fileType = "text/plain", fileData = Convert.ToBase64String("new private evidence"u8.ToArray()) });
        replacement.StatusCode.Should().Be(HttpStatusCode.OK);
        var version = (await replacement.Content.ReadFromJsonAsync<DocumentDTO>(Json))!;
        version.Scope.Should().Be("Personal");
        version.UserId.Should().Be(document.UserId);
        (await other.GetAsync($"/api/documents/{version.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await owner.GetFromJsonAsync<List<DocumentDTO>>($"/api/documents/contract/{second.Id}/documents", Json))!.Single().Id.Should().Be(version.Id);
        var addressResponse = await owner.PostAsJsonAsync("/api/addresses", new { entityType = "Contractor", contractorId = contractor.Id, street = "Private Street", city = "Berlin", zip = "12345", country = "Germany" });
        addressResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var address = (await addressResponse.Content.ReadFromJsonAsync<AddressDTO>(Json))!;
        (await other.GetAsync($"/api/addresses/{address.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await owner.GetAsync($"/api/addresses/{address.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await owner.PostAsJsonAsync("/api/addresses", new { entityType = "Tenant", street = "Private Street", city = "Berlin", zip = "12345", country = "Germany" })).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvalidWorkspaceClaims_AreRejectedDespiteValidSignatureAndSession(bool negative)
    {
        var (client, auth) = await Personal();
        var handler = new JwtSecurityTokenHandler();
        var original = handler.ReadJwtToken(auth.AccessToken);
        var claims = original.Claims.Where(c => c.Type != "TenantId" && c.Type != "access_scope").ToList();
        claims.Add(new Claim("TenantId", negative ? "-1" : "0"));
        if (negative) claims.Add(new Claim("access_scope", "personal"));
        var token = new JwtSecurityToken(original.Issuer, "QontractClients", claims,
            expires: DateTime.UtcNow.AddHours(1), signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.ASCII.GetBytes(JwtKeys.DevelopmentFallback)), SecurityAlgorithms.HmacSha256));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", handler.WriteToken(token));
        (await client.GetAsync("/api/contracts")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PersonalSwitch_PreservesExpiryAndLogoutRevokesAllWorkspaceTokens()
    {
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email = "admin@qontract.com", password = "password123" });
        var shared = (await login.Content.ReadFromJsonAsync<AuthDTO>(Json))!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", shared.AccessToken);
        var response = await client.PostAsJsonAsync("/api/auth/switch-tenant", new { tenantId = 0 });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var personal = (await response.Content.ReadFromJsonAsync<AuthDTO>(Json))!;
        personal.ExpiresAt.Should().Be(shared.ExpiresAt);
        personal.User!.Role.Should().Be("User");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", personal.AccessToken);
        (await client.GetFromJsonAsync<List<ContractDTO>>("/api/contracts", Json))!.Should().BeEmpty();
        (await client.PostAsync("/api/auth/logout", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", shared.AccessToken);
        (await client.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
