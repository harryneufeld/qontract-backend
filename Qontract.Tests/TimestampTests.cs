using FluentAssertions;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Qontract.Application.Models;
using Qontract.Domain.Models;
using Qontract.Infrastructure.Data;

namespace Qontract.Tests;

/// <summary>
/// Tests for the audit timestamps on all stored entities (issue #16):
/// every entity derives from BaseEntity and carries CreatedAt/ModifiedAt
/// plus CreatedBy/ModifiedBy. Timestamps are maintained automatically by
/// ApplicationDbContext: CreatedAt/ModifiedAt are stamped on insert and
/// ModifiedAt is refreshed on every update.
/// </summary>
public class TimestampTests : IClassFixture<QontractWebApplicationFactory>
{
    private readonly QontractWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public TimestampTests(QontractWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task<string> GetTokenAsync(string email)
    {
        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", new { email, password = "password123" });
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var auth = await loginResponse.Content.ReadFromJsonAsync<AuthDTO>();
        auth.Should().NotBeNull();
        return auth!.AccessToken;
    }

    private record ContractorJson(
        int Id,
        string Name,
        int TenantId,
        DateTime CreatedAt,
        DateTime ModifiedAt,
        string? CreatedBy,
        string? ModifiedBy);

    [Fact]
    public async Task CreateContractor_ShouldStampCreatedAtAndModifiedAt_WithCurrentIdentity()
    {
        var token = await GetTokenAsync("admin@qontract.com");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var before = DateTime.UtcNow;
        var response = await _client.PostAsJsonAsync("/api/contractors", new
        {
            name = "Timestamp Plumber",
            type = ContractorType.Individual.ToString()
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await response.Content.ReadFromJsonAsync<ContractorJson>();
        created.Should().NotBeNull();

        // Both timestamps must be stamped server-side, around now
        created!.CreatedAt.Should().BeOnOrAfter(before.AddSeconds(-5));
        created.ModifiedAt.Should().BeOnOrAfter(before.AddSeconds(-5));
        created.CreatedAt.Should().BeCloseTo(created.ModifiedAt, TimeSpan.FromSeconds(5));

        // Audit identity comes from the authenticated user
        created.CreatedBy.Should().Be("admin@qontract.com");
        created.ModifiedBy.Should().Be("admin@qontract.com");
    }

    [Fact]
    public async Task UpdateContractor_ShouldRefreshModifiedAt_ButKeepCreatedAt()
    {
        var token = await GetTokenAsync("admin@qontract.com");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var createResponse = await _client.PostAsJsonAsync("/api/contractors", new
        {
            name = "Timestamp Painter",
            type = ContractorType.Individual.ToString()
        });
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResponse.Content.ReadFromJsonAsync<ContractorJson>();
        created.Should().NotBeNull();

        // Give the two saves a measurable distance apart
        await Task.Delay(15);

        var updateResponse = await _client.PutAsJsonAsync($"/api/contractors/{created!.Id}", new
        {
            name = "Timestamp Painter GmbH",
            type = ContractorType.Company.ToString(),
            contactInfo = (string?)null,
            isActive = (bool?)null
        });
        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await updateResponse.Content.ReadFromJsonAsync<ContractorJson>();
        updated.Should().NotBeNull();

        // CreatedAt is immutable, ModifiedAt moves forward
        updated!.CreatedAt.Should().Be(created.CreatedAt);
        updated.ModifiedAt.Should().BeAfter(created.ModifiedAt);
        updated.ModifiedBy.Should().Be("admin@qontract.com");
    }

    [Fact]
    public async Task SeedTenants_ShouldCarryTimestamps_AndBaseEntityContract_HoldsAllAuditFields()
    {
        // All six entities derive from BaseEntity (compile-time guarantee of
        // the audit fields); verify at runtime that the seeded rows also
        // carried their timestamps through the same SaveChanges path.
        typeof(Tenant).BaseType.Should().Be(typeof(BaseEntity));
        typeof(User).BaseType.Should().Be(typeof(BaseEntity));
        typeof(Contractor).BaseType.Should().Be(typeof(BaseEntity));
        typeof(Contract).BaseType.Should().Be(typeof(BaseEntity));
        typeof(Document).BaseType.Should().Be(typeof(BaseEntity));
        typeof(Address).BaseType.Should().Be(typeof(BaseEntity));

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var tenant = await db.Tenants.FirstAsync();
        tenant.CreatedAt.Should().BeOnOrBefore(DateTime.UtcNow);
        tenant.ModifiedAt.Should().BeOnOrAfter(tenant.CreatedAt);
    }

    [Fact]
    public async Task DbContext_SaveChanges_ShouldStampInsertAndRefreshUpdate_Automatically()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var tenant = new Tenant
        {
            Name = $"Stamp Test {Guid.NewGuid():N}",
            Type = TenantType.Company,
            IsActive = true
        };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        var createdAt = tenant.CreatedAt;
        tenant.CreatedAt.Should().BeOnOrBefore(DateTime.UtcNow);
        tenant.ModifiedAt.Should().BeCloseTo(createdAt, TimeSpan.FromSeconds(1));

        await Task.Delay(15);

        tenant.Name = "Stamp Test Renamed";
        await db.SaveChangesAsync();

        // CreatedAt untouched by the update, ModifiedAt advanced
        tenant.CreatedAt.Should().Be(createdAt);
        tenant.ModifiedAt.Should().BeAfter(createdAt);

        // Clean up so the shared test database stays tidy
        db.Tenants.Remove(tenant);
        await db.SaveChangesAsync();
    }
}
