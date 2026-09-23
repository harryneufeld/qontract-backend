using FluentAssertions;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Xunit;
using Qontract.Application.Models;
using Qontract.Domain.Models;

namespace Qontract.Tests;

/// <summary>
/// End-to-end tests for the Contractor API: auth, validation, CRUD,
/// and cross-tenant isolation (the core multi-tenancy guarantee).
/// Tests within this class run sequentially, so mutating the shared
/// client's Authorization header per test is safe.
/// </summary>
public class ContractorApiIntegrationTests : IClassFixture<QontractWebApplicationFactory>
{
    private readonly HttpClient _client;

    public ContractorApiIntegrationTests(QontractWebApplicationFactory factory)
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

    private record ContractorJson(
        int Id,
        string Name,
        string? Type,
        string? Email,
        string? Phone,
        int TenantId,
        int? UserId,
        bool IsActive,
        DateTime CreatedAt,
        DateTime ModifiedAt,
        string? CreatedBy,
        string? ModifiedBy);

    private record AddressJson(
        int Id,
        string? EntityType,
        string? Street,
        string? City,
        string? Zip,
        string? Country);

    [Fact]
    public async Task GetContractors_ShouldReturnUnauthorized_WithoutToken()
    {
        _client.DefaultRequestHeaders.Authorization = null;

        var response = await _client.GetAsync("/api/contractors");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreateContractor_ShouldReturn400_WhenNameMissing()
    {
        var token = await GetTokenAsync("admin@qontract.com");
        Authenticate(token);

        var response = await _client.PostAsJsonAsync("/api/contractors", new
        {
            name = "",
            type = ContractorType.Individual.ToString(),
            email = (string?)null,
            phone = (string?)null
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateContractor_ShouldReturn400_WhenEmailMalformed()
    {
        var token = await GetTokenAsync("admin@qontract.com");
        Authenticate(token);

        var response = await _client.PostAsJsonAsync("/api/contractors", new
        {
            name = "Bad Email Contractor",
            type = ContractorType.Individual.ToString(),
            email = "not-an-email"
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ContractorCrud_ShouldCreateGetUpdateDeactivateAnd404Unknown()
    {
        var token = await GetTokenAsync("admin@qontract.com");
        Authenticate(token);

        // Create
        var createResponse = await _client.PostAsJsonAsync("/api/contractors", new
        {
            name = "Integration Plumber",
            type = ContractorType.Individual.ToString(),
            email = "plumber@example.de",
            phone = "012345 67890"
        });
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResponse.Content.ReadFromJsonAsync<ContractorJson>();
        created.Should().NotBeNull();
        created!.Name.Should().Be("Integration Plumber");
        created.IsActive.Should().BeTrue();
        created.Email.Should().Be("plumber@example.de");
        created.Phone.Should().Be("012345 67890");

        // Read back
        var getResponse = await _client.GetAsync($"/api/contractors/{created.Id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var fetched = await getResponse.Content.ReadFromJsonAsync<ContractorJson>();
        fetched.Should().NotBeNull();
        fetched!.Id.Should().Be(created.Id);
        fetched.Email.Should().Be("plumber@example.de");
        fetched.Phone.Should().Be("012345 67890");

        // Update
        var updateResponse = await _client.PutAsJsonAsync($"/api/contractors/{created.Id}", new
        {
            name = "Integration Plumber GmbH",
            type = ContractorType.Company.ToString(),
            email = (string?)null,
            phone = (string?)null,
            isActive = (bool?)null
        });
        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await updateResponse.Content.ReadFromJsonAsync<ContractorJson>();
        updated.Should().NotBeNull();
        updated!.Name.Should().Be("Integration Plumber GmbH");
        updated.Type.Should().Be(ContractorType.Company.ToString());

        // Deactivate
        var deactivateResponse = await _client.PatchAsync($"/api/contractors/{created.Id}/deactivate", null);
        deactivateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var deactivated = await deactivateResponse.Content.ReadFromJsonAsync<ContractorJson>();
        deactivated.Should().NotBeNull();
        deactivated!.IsActive.Should().BeFalse();

        // Unknown id -> 404
        var notFound = await _client.GetAsync("/api/contractors/999999");
        notFound.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetContractors_ShouldNotLeakAcrossTenants()
    {
        // Tenant 1 admin creates a contractor
        var tenant1Token = await GetTokenAsync("admin@qontract.com");
        Authenticate(tenant1Token);

        var createResponse = await _client.PostAsJsonAsync("/api/contractors", new
        {
            name = "Tenant1 Only Contractor",
            type = ContractorType.Individual.ToString()
        });
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResponse.Content.ReadFromJsonAsync<ContractorJson>();
        created.Should().NotBeNull();

        // Tenant 2 admin must not see or touch it
        var tenant2Token = await GetTokenAsync("admin2@qontract.com");
        Authenticate(tenant2Token);

        var list = await _client.GetFromJsonAsync<List<ContractorJson>>("/api/contractors");
        list.Should().NotBeNull();
        list!.Should().NotContain(c => c.Name == "Tenant1 Only Contractor");

        // Cross-tenant access by id must 404 (tenant-scoped lookup)
        var crossTenantGet = await _client.GetAsync($"/api/contractors/{created!.Id}");
        crossTenantGet.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var crossTenantDeactivate = await _client.PatchAsync($"/api/contractors/{created.Id}/deactivate", null);
        crossTenantDeactivate.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
