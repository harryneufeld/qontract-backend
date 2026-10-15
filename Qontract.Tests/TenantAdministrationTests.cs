using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Qontract.Application.Models;
using Qontract.Domain.Models;
using Qontract.Infrastructure.Data;
using Xunit;

namespace Qontract.Tests;

public class TenantAdministrationTests : IClassFixture<QontractWebApplicationFactory>
{
    private readonly QontractWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public TenantAdministrationTests(QontractWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task<AuthDTO> Login(string email)
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login",
            new { email, password = "password123" });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var auth = (await response.Content.ReadFromJsonAsync<AuthDTO>(JsonOptions))!;
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        return auth;
    }

    private async Task<TenantDTO> CreateTenant()
    {
        var response = await _client.PostAsJsonAsync("/api/tenants",
            new { name = $"Membership {Guid.NewGuid():N}", type = "Family" });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<TenantDTO>(JsonOptions))!;
    }

    [Fact]
    public async Task List_OnlyIncludesAdministeredTenants()
    {
        var auth = await Login("admin@qontract.com");
        var list = await _client.GetFromJsonAsync<List<TenantDTO>>("/api/tenants", JsonOptions);
        list.Should().Contain(t => t.Id == auth.User!.TenantId);
        list.Should().NotContain(t => t.Name == "Test Family Two");
    }

    [Theory]
    [InlineData("/api/tenants")]
    [InlineData("/api/v1/tenants")]
    public async Task ForeignAndUnknownIds_Return404_AndDoNotModifyTenant(string route)
    {
        await Login("admin2@qontract.com");
        var foreign = await CreateTenant();
        await Login("admin@qontract.com");

        foreach (var id in new[] { foreign.Id, int.MaxValue })
        {
            (await _client.GetAsync($"{route}/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await _client.PutAsJsonAsync($"{route}/{id}", new { name = "Intrusion", isActive = false }))
                .StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await _client.PatchAsync($"{route}/{id}/deactivate", null))
                .StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        await Login("admin2@qontract.com");
        var unchanged = await _client.GetFromJsonAsync<TenantDTO>($"/api/tenants/{foreign.Id}", JsonOptions);
        unchanged!.Name.Should().Be(foreign.Name);
        unchanged.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Create_AddsCreatorMembership_WithoutChangingPrimaryTenant()
    {
        var auth = await Login("admin@qontract.com");
        var tenant = await CreateTenant();
        (await _client.PutAsJsonAsync($"/api/tenants/{tenant.Id}", new { name = "Updated owned tenant" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        var list = await _client.GetFromJsonAsync<List<TenantDTO>>("/api/tenants", JsonOptions);
        list.Should().Contain(t => t.Id == auth.User!.TenantId);
        list.Should().Contain(t => t.Id == tenant.Id);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var membership = await db.TenantMemberships.SingleAsync(m => m.TenantId == tenant.Id && m.UserId == auth.UserId);
        membership.Role.Should().Be(UserRole.TenantAdmin);
        membership.IsActive.Should().BeTrue();
        (await db.Users.FindAsync(auth.UserId))!.TenantId.Should().Be(auth.User!.TenantId);
    }

    [Theory]
    [InlineData(false, UserRole.TenantAdmin)]
    [InlineData(true, UserRole.User)]
    public async Task RevokedOrNonAdminMembership_DeniesPreviouslyIssuedAdminToken(bool active, UserRole role)
    {
        var auth = await Login("admin@qontract.com");
        var tenant = await CreateTenant();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var membership = await db.TenantMemberships.SingleAsync(m => m.UserId == auth.UserId && m.TenantId == tenant.Id);
            membership.IsActive = active;
            membership.Role = role;
            await db.SaveChangesAsync();
        }

        (await _client.GetAsync($"/api/tenants/{tenant.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _client.PutAsJsonAsync($"/api/tenants/{tenant.Id}", new { name = "Revoked write" }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _client.PatchAsync($"/api/tenants/{tenant.Id}/deactivate", null))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        var list = await _client.GetFromJsonAsync<List<TenantDTO>>("/api/tenants", JsonOptions);
        list.Should().NotContain(t => t.Id == tenant.Id);
    }

    [Fact]
    public async Task DeactivatedAccount_CannotAdministerOrCreateTenants_WithOldToken()
    {
        var admin = await Login("admin@qontract.com");
        // Use a separate account so fixture seed accounts remain available.
        var email = $"deactivated-{Guid.NewGuid():N}@qontract.com";
        await TestAccounts.CreateMemberAsync(_factory.Services, admin.User!.TenantId, UserRole.TenantAdmin, email: email);
        var auth = await Login(email);
        var tenant = await CreateTenant();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            (await db.Users.FindAsync(auth.UserId))!.IsActive = false;
            await db.SaveChangesAsync();
        }

        (await _client.GetAsync($"/api/tenants/{tenant.Id}")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await _client.GetAsync("/api/tenants")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await _client.PostAsJsonAsync("/api/tenants", new { name = "Unauthorized tenant", type = "Family" }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        using var checkScope = _factory.Services.CreateScope();
        var checkDb = checkScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await checkDb.Tenants.AnyAsync(t => t.Name == "Unauthorized tenant")).Should().BeFalse();
    }
}
