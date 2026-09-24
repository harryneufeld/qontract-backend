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
        string? Currency,
        string? Value,
        string? ContractNumber,
        string? MinimumContractDate,
        int? ContractorId,
        DateTime CreatedAt,
        DateTime ModifiedAt,
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
            currency = "EUR",
            value = "850.00",
            contractNumber = "INT-2026-0001"
        });
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResponse.Content.ReadFromJsonAsync<ContractJson>();
        created.Should().NotBeNull();
        created!.Title.Should().Be("Integration Lease");
        created.Status.Should().Be("Active");
        created.ContractNumber.Should().Be("INT-2026-0001");
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
    public async Task ContractNumber_ShouldRoundTripThroughCreateGetAndUpdate()
    {
        var token = await GetTokenAsync("admin@qontract.com");
        Authenticate(token);

        // Without a contract number the field must be null
        var blankResponse = await _client.PostAsJsonAsync("/api/contracts", new
        {
            title = "No Number",
            status = "Active"
        });
        blankResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var blank = await blankResponse.Content.ReadFromJsonAsync<ContractJson>();
        blank!.ContractNumber.Should().BeNull();

        // Create with a contract number
        var createResponse = await _client.PostAsJsonAsync("/api/contracts", new
        {
            title = "Numbered Contract",
            status = "Active",
            contractNumber = "CNT-2026-0042"
        });
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResponse.Content.ReadFromJsonAsync<ContractJson>();
        created!.ContractNumber.Should().Be("CNT-2026-0042");

        // Read back: the number survives persistence
        var getResponse = await _client.GetAsync($"/api/contracts/{created.Id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var fetched = await getResponse.Content.ReadFromJsonAsync<ContractJson>();
        fetched!.ContractNumber.Should().Be("CNT-2026-0042");

        // The number also appears in the list endpoint
        var listResponse = await _client.GetFromJsonAsync<List<ContractJson>>("/api/contracts");
        listResponse.Should().NotBeNull();
        var inList = listResponse!.First(c => c.Id == created.Id);
        inList.ContractNumber.Should().Be("CNT-2026-0042");

        // Update to a new number
        var updateResponse = await _client.PutAsJsonAsync($"/api/contracts/{created.Id}", new
        {
            contractNumber = "CNT-2026-0099"
        });
        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await updateResponse.Content.ReadFromJsonAsync<ContractJson>();
        updated!.ContractNumber.Should().Be("CNT-2026-0099");

        // A partial update without contractNumber must not clear it
        var partialResponse = await _client.PutAsJsonAsync($"/api/contracts/{created.Id}", new
        {
            title = "Numbered Contract (v2)"
        });
        partialResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var partial = await partialResponse.Content.ReadFromJsonAsync<ContractJson>();
        partial!.ContractNumber.Should().Be("CNT-2026-0099");

        // Deactivate response still carries the number
        var deactivateResponse = await _client.PatchAsync(
            $"/api/contracts/{created.Id}/deactivate",
            new StringContent(""));
        deactivateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var deactivated = await deactivateResponse.Content.ReadFromJsonAsync<ContractJson>();
        deactivated!.ContractNumber.Should().Be("CNT-2026-0099");
    }

    [Fact]
    public async Task MinimumContractDate_ShouldRoundTripThroughCreateGetAndUpdate()
    {
        var token = await GetTokenAsync("admin@qontract.com");
        Authenticate(token);

        // Without a minimum contract date the field must be null
        var blankResponse = await _client.PostAsJsonAsync("/api/contracts", new
        {
            title = "No Minimum Date",
            status = "Active"
        });
        blankResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var blank = await blankResponse.Content.ReadFromJsonAsync<ContractJson>();
        blank!.MinimumContractDate.Should().BeNull();

        // Create with a minimum contract date
        var createResponse = await _client.PostAsJsonAsync("/api/contracts", new
        {
            title = "Minimum Date Contract",
            status = "Active",
            minimumContractDate = "2027-06-30"
        });
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResponse.Content.ReadFromJsonAsync<ContractJson>();
        created!.MinimumContractDate.Should().Be("2027-06-30");

        // Read back: the date survives persistence
        var getResponse = await _client.GetAsync($"/api/contracts/{created.Id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var fetched = await getResponse.Content.ReadFromJsonAsync<ContractJson>();
        fetched!.MinimumContractDate.Should().Be("2027-06-30");

        // The date also appears in the list endpoint
        var listResponse = await _client.GetFromJsonAsync<List<ContractJson>>("/api/contracts");
        listResponse.Should().NotBeNull();
        var inList = listResponse!.First(c => c.Id == created.Id);
        inList.MinimumContractDate.Should().Be("2027-06-30");

        // Update to a new minimum date
        var updateResponse = await _client.PutAsJsonAsync($"/api/contracts/{created.Id}", new
        {
            minimumContractDate = "2028-01-31"
        });
        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await updateResponse.Content.ReadFromJsonAsync<ContractJson>();
        updated!.MinimumContractDate.Should().Be("2028-01-31");

        // A partial update without minimumContractDate must not clear it
        var partialResponse = await _client.PutAsJsonAsync($"/api/contracts/{created.Id}", new
        {
            title = "Minimum Date Contract (v2)"
        });
        partialResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var partial = await partialResponse.Content.ReadFromJsonAsync<ContractJson>();
        partial!.MinimumContractDate.Should().Be("2028-01-31");

        // Deactivate response still carries the date
        var deactivateResponse = await _client.PatchAsync(
            $"/api/contracts/{created.Id}/deactivate",
            new StringContent(""));
        deactivateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var deactivated = await deactivateResponse.Content.ReadFromJsonAsync<ContractJson>();
        deactivated!.MinimumContractDate.Should().Be("2028-01-31");
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

        var crossTenantDelete = await _client.DeleteAsync($"/api/contracts/{created!.Id}");
        crossTenantDelete.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetContracts_WithPaging_ShouldPageAndStayTenantIsolated()
    {
        var token = await GetTokenAsync("admin@qontract.com");
        Authenticate(token);

        // Distinctive prefix so these are identifiable regardless of the
        // shared DB's other (pre-existing) contracts.
        const string prefix = "PAGING-PROBE-";
        var ids = new List<int>();
        for (var i = 1; i <= 5; i++)
        {
            var resp = await _client.PostAsJsonAsync("/api/contracts", new
            {
                title = $"{prefix}{i}",
                status = "Active"
            });
            resp.StatusCode.Should().Be(HttpStatusCode.Created);
            ids.Add((await resp.Content.ReadFromJsonAsync<ContractJson>())!.Id);
        }

        // Full list (no paging params) still returns every probe contract
        // and remains backward compatible (a plain JSON array).
        var all = await _client.GetFromJsonAsync<List<ContractJson>>("/api/contracts");
        all.Should().NotBeNull();
        var allProbes = all!
            .Where(c => c.Title.StartsWith(prefix))
            .Select(c => c.Id)
            .ToHashSet();
        allProbes.Should().BeEquivalentTo(ids);

        // Page 1 of 2: exactly 2 probe rows, newest-first, disjoint from
        // page 2. (Newest-first because the list sorts by CreatedAt desc,
        // with Id desc as a stable tiebreak.)
        var page1 = await _client.GetFromJsonAsync<List<ContractJson>>(
            "/api/contracts?page=1&pageSize=2");
        page1.Should().NotBeNull();
        page1!.Should().OnlyContain(c => c.Title.StartsWith(prefix));
        page1.Should().HaveCount(2);
        page1[0].Id.Should().BeGreaterThan(page1[1].Id);

        var page2 = await _client.GetFromJsonAsync<List<ContractJson>>(
            "/api/contracts?page=2&pageSize=2");
        page2!.Should().OnlyContain(c => c.Title.StartsWith(prefix));
        page2.Should().HaveCount(2);

        // The two pages are disjoint and together hold 4 distinct probe ids
        // (page 3 would carry the 5th).
        var pageIds = page1.Select(c => c.Id).Concat(page2.Select(c => c.Id)).ToList();
        pageIds.Distinct().Should().HaveCount(4);
        (page1.Select(c => c.Id).Intersect(page2.Select(c => c.Id))).Should().BeEmpty();

        // Tenant 2 must see no probe contracts even while paging.
        var tenant2Token = await GetTokenAsync("admin2@qontract.com");
        Authenticate(tenant2Token);
        var tenant2Page = await _client.GetFromJsonAsync<List<ContractJson>>(
            "/api/contracts?page=1&pageSize=50");
        tenant2Page!.Should().NotContain(c => c.Title.StartsWith(prefix));
    }
}
