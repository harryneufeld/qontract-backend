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
using Qontract.Domain.Models;
using Qontract.Infrastructure.Data;
using Xunit;

namespace Qontract.Tests;

public class AccountTenantAddressTests(QontractWebApplicationFactory factory) : IClassFixture<QontractWebApplicationFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
        { Converters = { new JsonStringEnumConverter() } };

    private async Task<(HttpClient Admin, HttpClient Member, int TenantId, int AdminId)> Tenant()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tenant = new Tenant { Name = "Address tenant", IsActive = true };
        db.Tenants.Add(tenant); await db.SaveChangesAsync();
        var admin = new User { Name = "Address admin", Email = $"admin-{Guid.NewGuid():N}@example.test",
            PasswordHash = Passwords.Hash("address-password123"), TenantId = tenant.Id, Role = UserRole.TenantAdmin, IsActive = true };
        var member = new User { Name = "Address member", Email = $"member-{Guid.NewGuid():N}@example.test",
            PasswordHash = admin.PasswordHash, TenantId = tenant.Id, Role = UserRole.User, IsActive = true };
        db.Users.AddRange(admin, member); await db.SaveChangesAsync();
        return (await Login(admin.Email), await Login(member.Email), tenant.Id, admin.Id);
    }

    private async Task<HttpClient> Login(string email)
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "address-password123" });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var auth = (await response.Content.ReadFromJsonAsync<AuthDTO>(Json))!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        return client;
    }

    private static async Task<AddressDTO> Add(HttpClient client, string type)
    {
        var response = await client.PostAsJsonAsync("/api/addresses", new { entityType = type,
            userId = 1, tenantId = 1, street = " Straße with whitespace ", houseNumber = " 12a/4 ", city = "Berlin", zip = "12345", country = "Germany" });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<AddressDTO>(Json))!;
    }

    [Fact]
    public async Task AccountWithoutAnyTenant_CanManagePrivateAddresses()
    {
        var email = $"no-tenant-{Guid.NewGuid():N}@example.test";
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Users.Add(new User { Name = "Personal account", Email = email,
                PasswordHash = Passwords.Hash("address-password123"), Role = UserRole.User, IsActive = true });
            await db.SaveChangesAsync();
        }
        var client = await Login(email);
        var address = await Add(client, "Person");
        (await client.GetFromJsonAsync<List<AddressDTO>>("/api/addresses/me", Json))!.Single().Id.Should().Be(address.Id);
        (await client.GetAsync("/api/addresses/tenant")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.PutAsJsonAsync($"/api/addresses/{address.Id}", new { entityType = "Person", houseNumber = "7" })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PatchAsync($"/api/addresses/{address.Id}/deactivate", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetFromJsonAsync<List<AddressDTO>>("/api/addresses/me", Json))!.Should().BeEmpty();
    }

    [Fact]
    public async Task AccountAddress_IsPrivateEvenFromTenantAdmin_AndFollowsOwnerAcrossWorkspaces()
    {
        var (admin, member, _, adminId) = await Tenant();
        var privateAddress = await Add(member, "Person");
        (await admin.GetAsync($"/api/addresses/{privateAddress.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await admin.PutAsJsonAsync($"/api/addresses/{privateAddress.Id}", new { entityType = "Person", city = "Stolen" })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await admin.PatchAsync($"/api/addresses/{privateAddress.Id}/deactivate", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await admin.GetFromJsonAsync<List<AddressDTO>>("/api/addresses/me", Json))!.Should().BeEmpty();
        var rows = (await member.GetFromJsonAsync<List<AddressDTO>>("/api/addresses/me", Json))!;
        rows.Single().Street.Should().Be(" Straße with whitespace ");
        rows.Single().HouseNumber.Should().Be(" 12a/4 ");
        var switched = await member.PostAsJsonAsync("/api/auth/switch-tenant", new { tenantId = 0 });
        var auth = (await switched.Content.ReadFromJsonAsync<AuthDTO>(Json))!;
        member.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        (await member.GetAsync($"/api/addresses/{privateAddress.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await member.GetFromJsonAsync<List<AddressDTO>>("/api/addresses/me", Json))!.Single().Id.Should().Be(privateAddress.Id);
        using var scope = factory.Services.CreateScope();
        var stored = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Addresses.FindAsync(privateAddress.Id);
        stored!.UserId.Should().Be(auth.UserId).And.NotBe(adminId);
        stored.TenantId.Should().BeNull();
    }

    [Fact]
    public async Task TenantAddress_MembersRead_AdminsWrite_ForeignTenantsCannotAccess()
    {
        var (admin, member, tenantId, _) = await Tenant();
        var (foreign, _, _, _) = await Tenant();
        var shared = await Add(admin, "Tenant");
        (await member.GetFromJsonAsync<List<AddressDTO>>("/api/addresses/tenant", Json))!.Single().Id.Should().Be(shared.Id);
        (await member.GetAsync($"/api/addresses/{shared.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await member.PostAsJsonAsync("/api/addresses", new { entityType = "Tenant", street = "Not allowed" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await member.PutAsJsonAsync($"/api/addresses/{shared.Id}", new { entityType = "Person", street = "Not allowed" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await member.PatchAsync($"/api/addresses/{shared.Id}/deactivate", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await foreign.GetAsync($"/api/addresses/{shared.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await foreign.PutAsJsonAsync($"/api/addresses/{shared.Id}", new { street = "Not allowed" })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await admin.PutAsJsonAsync($"/api/addresses/{shared.Id}", new { entityType = "Tenant", houseNumber = "14" })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await admin.PatchAsync($"/api/addresses/{shared.Id}/deactivate", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await member.GetFromJsonAsync<List<AddressDTO>>("/api/addresses/tenant", Json))!.Should().BeEmpty();
        using var scope = factory.Services.CreateScope();
        var stored = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Addresses.FindAsync(shared.Id);
        stored!.TenantId.Should().Be(tenantId); stored.UserId.Should().BeNull();
        stored.Street.Should().Be(" Straße with whitespace "); stored.HouseNumber.Should().Be("14"); stored.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task PartialAddress_OptionalClearing_AndUnknownHistoricalOwners_AreHandledWithoutGuessing()
    {
        var (admin, _, _, adminId) = await Tenant();
        var response = await admin.PostAsJsonAsync("/api/addresses", new { entityType = "Person", city = "Only city known" });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var partial = (await response.Content.ReadFromJsonAsync<AddressDTO>(Json))!;
        partial.Street.Should().BeNull(); partial.HouseNumber.Should().BeNull();
        (await admin.PutAsJsonAsync($"/api/addresses/{partial.Id}", new { entityType = "Person", city = "", country = "Germany" })).StatusCode.Should().Be(HttpStatusCode.OK);
        var changed = (await admin.GetFromJsonAsync<AddressDTO>($"/api/addresses/{partial.Id}", Json))!;
        changed.City.Should().BeEmpty(); changed.Country.Should().Be("Germany");
        (await admin.PutAsJsonAsync($"/api/addresses/{partial.Id}", new { country = "" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var unresolved = new Address { EntityType = EntityType.Person, Street = "Legacy unresolved", CreatedBy = "admin@example.test" };
        db.Addresses.Add(unresolved); await db.SaveChangesAsync();
        (await admin.GetAsync($"/api/addresses/{unresolved.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        unresolved.UserId.Should().BeNull();
        (await db.Users.FindAsync(adminId))!.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task LongImportedFields_StayVerbatimWhenOtherFieldsAreEdited()
    {
        var (admin, _, _, adminId) = await Tenant();
        var street = " " + new string('Ä', 600) + " ";
        Address legacy;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            legacy = new Address { EntityType = EntityType.Person, UserId = adminId, Street = street, City = new string('ß', 120), HouseNumber = "12/a" };
            db.Addresses.Add(legacy); await db.SaveChangesAsync();
        }
        var response = await admin.PutAsJsonAsync($"/api/addresses/{legacy.Id}", new { entityType = "Person", street, city = legacy.City, zip = "12345" });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = (await response.Content.ReadFromJsonAsync<AddressDTO>(Json))!;
        updated.Street.Should().Be(street); updated.City.Should().Be(legacy.City); updated.Zip.Should().Be("12345");
        (await admin.PutAsJsonAsync($"/api/addresses/{legacy.Id}", new { street = new string('X', 601) })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
