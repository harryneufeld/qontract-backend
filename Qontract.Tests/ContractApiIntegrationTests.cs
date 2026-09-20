using FluentAssertions;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Xunit;
using Qontract.Application.Models;

namespace Qontract.Tests;

/// <summary>
/// End-to-end tests for the Contracts API: auth, CRUD, and cross-tenant
/// isolation (the core multi-tenancy guarantee).
/// Tests within this class run sequentially, so mutating the shared
/// client's Authorization header per test is safe.
/// </summary>
public class ContractApiIntegrationTests : IClassFixture<QontractWebApplicationFactory>
{
    private readonly HttpClient _client;

    public ContractApiIntegrationTests(QontractWebApplicationFactory factory)
    {
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

    private void Authenticate(string token)
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    private record ContractJson(
        int Id,
        string Title,
        string? Description,
        string? Status,
        string? StartDate,
        string? EndDate,
        string? Counterparty,
        string? Currency,
        string? Value,
        int? ContractorId,
        DateTime CreatedAt,
        DateTime UpdatedAt,
        string? CreatedBy,
        string? ModifiedBy);

    [Fact]
    public async Task GetContracts_ShouldReturnUnauthorized_WithoutToken()
    {
        _client.DefaultRequestHeaders.Authorization = null;

        var response = await _client.GetAsync("/api/contracts");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreateContract_ShouldReturn400_WhenTitleMissing()
    {
        var token = await GetTokenAsync("admin@qontract.com");
        Authenticate(token);

        var response = await _client.PostAsJsonAsync("/api/contracts", new
        {
            title = "",
            status = "Active"
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ContractCrud_ShouldCreateGetUpdateDeleteAnd404Unknown()
    {
        var token = await GetTokenAsync("admin@qontract.com");
        Authenticate(token);

        // Create (status as string — the API serializes enums as strings)
        var createResponse = await _client.PostAsJsonAsync("/api/contracts", new
        {
            title = "Integration Lease",
            description = "Lease created by the contract integration tests",
            status = "Active",
            startDate = "2026-01-01",
            endDate = "2027-01-01",
            counterparty = "Acme Rentals",
            currency = "EUR",
            value = "850.00"
        });
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResponse.Content.ReadFromJsonAsync<ContractJson>();
        created.Should().NotBeNull();
        created!.Title.Should().Be("Integration Lease");
        created.Status.Should().Be("Active");
        created.ContractorId.Should().BeNull(); // no contractor required

        // Read back
        var getResponse = await _client.GetAsync($"/api/contracts/{created.Id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var fetched = await getResponse.Content.ReadFromJsonAsync<ContractJson>();
        fetched.Should().NotBeNull();
        fetched!.Id.Should().Be(created.Id);

        // Update
        var updateResponse = await _client.PutAsJsonAsync($"/api/contracts/{created.Id}", new
        {
            title = "Integration Lease (v2)",
            status = "Draft"
        });
        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await updateResponse.Content.ReadFromJsonAsync<ContractJson>();
        updated.Should().NotBeNull();
        updated!.Title.Should().Be("Integration Lease (v2)");
        updated.Status.Should().Be("Draft");

        // Soft delete -> subsequent reads must 404
        var deleteResponse = await _client.DeleteAsync($"/api/contracts/{created.Id}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var afterDelete = await _client.GetAsync($"/api/contracts/{created.Id}");
        afterDelete.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Unknown id -> 404
        var notFound = await _client.GetAsync("/api/contracts/999999");
        notFound.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Contracts_ShouldNotLeakAcrossTenants()
    {
        // Tenant 1 admin creates a contract
        var tenant1Token = await GetTokenAsync("admin@qontract.com");
        Authenticate(tenant1Token);

        var createResponse = await _client.PostAsJsonAsync("/api/contracts", new
        {
            title = "Tenant1 Only Contract",
            status = "Active"
        });
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResponse.Content.ReadFromJsonAsync<ContractJson>();
        created.Should().NotBeNull();

        // Tenant 2 admin must not see or touch it
        var tenant2Token = await GetTokenAsync("admin2@qontract.com");
        Authenticate(tenant2Token);

        var list = await _client.GetFromJsonAsync<List<ContractJson>>("/api/contracts");
        list.Should().NotBeNull();
        list!.Should().NotContain(c => c.Title == "Tenant1 Only Contract");

        // Cross-tenant access by id must 404 (tenant-scoped lookup)
        var crossTenantGet = await _client.GetAsync($"/api/contracts/{created!.Id}");
        crossTenantGet.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var crossTenantUpdate = await _client.PutAsJsonAsync($"/api/contracts/{created.Id}", new
        {
            title = "Hijacked by tenant 2"
        });
        crossTenantUpdate.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var crossTenantDelete = await _client.DeleteAsync($"/api/contracts/{created.Id}");
        crossTenantDelete.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
