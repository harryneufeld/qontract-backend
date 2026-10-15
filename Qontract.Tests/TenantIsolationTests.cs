using FluentAssertions;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;
using Qontract.Application.Models;

namespace Qontract.Tests;

/// <summary>
/// Regression tests for issue #32 (tenant isolation): no endpoint may read
/// or modify data of a tenant other than the caller's.
///
/// - User deactivation must be tenant-scoped (was a cross-tenant write).
/// - User creation must not trust the client-supplied TenantId.
/// - Address read/update/deactivate must be tenant-scoped (had no tenant
///   check at all); contractor addresses must link to a contractor of the
///   caller's tenant (were created orphaned with ContractorId = 0).
/// - Tenant create/update must record the acting user in the audit fields
///   (were hardcoded to null).
/// </summary>
public class TenantIsolationTests : IClassFixture<QontractWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly QontractWebApplicationFactory _factory;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public TenantIsolationTests(QontractWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task<string> GetTokenAsync(string email)
    {
        var login = await _client.PostAsJsonAsync(
            "/api/auth/login", new { email, password = "password123" });
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        var auth = await login.Content.ReadFromJsonAsync<AuthDTO>();
        auth.Should().NotBeNull();
        return auth!.AccessToken;
    }

    private void Authenticate(string token)
    {
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
    }

    /// <summary>
    /// Creates a contractor for the currently authenticated tenant and
    /// returns its DTO (used to build contractor addresses).
    /// </summary>
    private async Task<ContractorDTO> CreateContractorAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/contractors", new
        {
            name = $"TI-{Guid.NewGuid():N}",
            type = "Individual",
            email = (string?)null,
            phone = (string?)null
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var contractor = await response.Content.ReadFromJsonAsync<ContractorDTO>(JsonOptions);
        contractor.Should().NotBeNull();
        return contractor!;
    }

    // -----------------------------------------------------------------
    // 1. User deactivation is tenant-scoped
    // -----------------------------------------------------------------

    [Fact]
    public async Task DeactivateUser_CrossTenant_Returns404_AndUserStaysActive()
    {
        var token2 = await GetTokenAsync("admin2@qontract.com");
        Authenticate(token2);

        var tenant2 = (await (await _client.GetAsync("/api/auth/me")).Content.ReadFromJsonAsync<AuthUserDTO>(JsonOptions))!.TenantId;
        var victim = await TestAccounts.CreateMemberAsync(_factory.Services, tenant2);

        // Tenant 1's admin tries to deactivate tenant 2's user.
        var token1 = await GetTokenAsync("admin@qontract.com");
        Authenticate(token1);
        var deact = await _client.PatchAsync($"/api/users/{victim.Id}/deactivate", new StringContent(""));
        deact.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // And the deactivation must NOT have been committed: the user is
        // still active and visible to tenant 2.
        Authenticate(token2);
        var get = await _client.GetAsync($"/api/users/{victim.Id}");
        get.StatusCode.Should().Be(HttpStatusCode.OK);
        var after = await get.Content.ReadFromJsonAsync<UserDTO>(JsonOptions);
        after.Should().NotBeNull();
        after!.IsActive.Should().BeTrue();
    }

    // -----------------------------------------------------------------
    // 2. (removed) Administrators no longer create accounts over the API;
    //    members join through invitations (issue #80).
    // -----------------------------------------------------------------

    // -----------------------------------------------------------------
    // 3. Tenant create/update record the acting user in audit fields
    // -----------------------------------------------------------------

    [Fact]
    public async Task TenantCreateAndUpdate_RecordActingUserInAuditFields()
    {
        var token1 = await GetTokenAsync("admin@qontract.com");
        Authenticate(token1);

        var create = await _client.PostAsJsonAsync(
            "/api/tenants", new { name = $"TI {Guid.NewGuid():N}", type = "Family" });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var tenant = await create.Content.ReadFromJsonAsync<TenantDTO>(JsonOptions);
        tenant.Should().NotBeNull();
        tenant!.CreatedBy.Should().Be("admin@qontract.com");
        tenant.ModifiedBy.Should().Be("admin@qontract.com");

        var update = await _client.PutAsJsonAsync($"/api/tenants/{tenant.Id}", new
        {
            name = tenant.Name,
            type = (string?)null,
            settings = (string?)null,
            isActive = (bool?)null
        });
        update.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await update.Content.ReadFromJsonAsync<TenantDTO>(JsonOptions);
        updated.Should().NotBeNull();
        updated!.ModifiedBy.Should().Be("admin@qontract.com");
    }

    // -----------------------------------------------------------------
    // 4. Address read/update/deactivate are tenant-scoped
    // -----------------------------------------------------------------

    [Fact]
    public async Task Address_CrossTenant_GetUpdateDeactivate_All404()
    {
        var token2 = await GetTokenAsync("admin2@qontract.com");
        Authenticate(token2);
        var create = await _client.PostAsJsonAsync("/api/addresses", new
        {
            entityType = "Tenant",
            street = "Secret Street 1",
            city = "Berlin",
            zip = "10115",
            country = "Germany"
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var address = await create.Content.ReadFromJsonAsync<AddressDTO>(JsonOptions);
        address.Should().NotBeNull();

        // Tenant 1 must not be able to read, modify, or deactivate it.
        var token1 = await GetTokenAsync("admin@qontract.com");
        Authenticate(token1);

        var get = await _client.GetAsync($"/api/addresses/{address!.Id}");
        get.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var put = await _client.PutAsJsonAsync($"/api/addresses/{address.Id}", new
        {
            entityType = "Tenant",
            street = "Hacked Street",
            city = "Nowhere",
            zip = "00000",
            country = "Nowhereland"
        });
        put.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var deact = await _client.PatchAsync($"/api/addresses/{address.Id}/deactivate", new StringContent(""));
        deact.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Unchanged and active in tenant 2.
        Authenticate(token2);
        var get2 = await _client.GetAsync($"/api/addresses/{address.Id}");
        get2.StatusCode.Should().Be(HttpStatusCode.OK);
        var after = await get2.Content.ReadFromJsonAsync<AddressDTO>(JsonOptions);
        after!.Street.Should().Be("Secret Street 1");
        after.IsActive.Should().BeTrue();
    }

    // -----------------------------------------------------------------
    // 5. Contractor addresses link to a real contractor of the caller
    // -----------------------------------------------------------------

    [Fact]
    public async Task Address_Contractor_LinksToOwningContractor_AndCrossTenantContractor404()
    {
        var token1 = await GetTokenAsync("admin@qontract.com");
        Authenticate(token1);
        var contractor = await CreateContractorAsync();

        // A contractor of the caller's tenant: OK.
        var ok = await _client.PostAsJsonAsync("/api/addresses", new
        {
            entityType = "Contractor",
            contractorId = contractor.Id,
            street = "Crew Base 1",
            city = "Berlin",
            zip = "10115",
            country = "Germany"
        });
        ok.StatusCode.Should().Be(HttpStatusCode.Created);
        var address = await ok.Content.ReadFromJsonAsync<AddressDTO>(JsonOptions);
        address.Should().NotBeNull();
        address!.EntityType.Should().Be(Qontract.Domain.Models.EntityType.Contractor);

        // A contractor of another tenant (seeded for tenant 2): 404.
        var token2 = await GetTokenAsync("admin2@qontract.com");
        Authenticate(token2);
        var foreignContractor = await CreateContractorAsync();

        var foreign = await _client.PostAsJsonAsync("/api/addresses", new
        {
            entityType = "Contractor",
            contractorId = contractor.Id, // tenant 1's contractor
            street = "Intrusion 1",
            city = "Berlin",
            zip = "10115",
            country = "Germany"
        });
        foreign.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // And tenant 1 cannot see tenant 2's contractor address either:
        // tenant 2 creates one for its own contractor.
        var t2address = await _client.PostAsJsonAsync("/api/addresses", new
        {
            entityType = "Contractor",
            contractorId = foreignContractor.Id,
            street = "T2 Base 1",
            city = "Hamburg",
            zip = "20095",
            country = "Germany"
        });
        t2address.StatusCode.Should().Be(HttpStatusCode.Created);
        var t2 = await t2address.Content.ReadFromJsonAsync<AddressDTO>(JsonOptions);

        Authenticate(token1);
        var get1 = await _client.GetAsync($"/api/addresses/{t2!.Id}");
        get1.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // -----------------------------------------------------------------
    // 5b. Contractor address list endpoint (issue #41)
    // -----------------------------------------------------------------

    [Fact]
    public async Task ContractorAddresses_ReturnsOwnAddresses_HidesDeactivated_404sCrossTenant()
    {
        var token1 = await GetTokenAsync("admin@qontract.com");
        Authenticate(token1);
        var contractor = await CreateContractorAsync();

        // Two active addresses + one that gets deactivated.
        var a1 = await _client.PostAsJsonAsync("/api/addresses", new
        {
            entityType = "Contractor",
            contractorId = contractor.Id,
            street = "Base Street 1",
            city = "Berlin",
            zip = "10115",
            country = "Germany"
        });
        a1.StatusCode.Should().Be(HttpStatusCode.Created);
        var addr1 = await a1.Content.ReadFromJsonAsync<AddressDTO>(JsonOptions);

        var a2 = await _client.PostAsJsonAsync("/api/addresses", new
        {
            entityType = "Contractor",
            contractorId = contractor.Id,
            street = "Base Street 2",
            city = "Potsdam",
            zip = "14467",
            country = "Germany"
        });
        a2.StatusCode.Should().Be(HttpStatusCode.Created);
        var addr2 = await a2.Content.ReadFromJsonAsync<AddressDTO>(JsonOptions);

        // Owner tenant: sees exactly its two active addresses.
        var list = await _client.GetFromJsonAsync<List<AddressDTO>>($"/api/contractors/{contractor.Id}/addresses", JsonOptions);
        list.Should().NotBeNull();
        list!.Select(a => a.Id).Should().BeEquivalentTo(new[] { addr1!.Id, addr2!.Id });

        // Deactivated address disappears from the list.
        var deact = await _client.PatchAsync($"/api/addresses/{addr1.Id}/deactivate", null);
        deact.StatusCode.Should().Be(HttpStatusCode.OK);

        var listAfterDeactivate = await _client.GetFromJsonAsync<List<AddressDTO>>($"/api/contractors/{contractor.Id}/addresses", JsonOptions);
        listAfterDeactivate!.Select(a => a.Id).Should().BeEquivalentTo(new[] { addr2!.Id });

        // Cross-tenant: tenant 2 gets 404 for tenant 1's contractor (no leak).
        var token2 = await GetTokenAsync("admin2@qontract.com");
        Authenticate(token2);
        var cross = await _client.GetAsync($"/api/contractors/{contractor.Id}/addresses");
        cross.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Unknown contractor id: 404 as well.
        var unknown = await _client.GetAsync("/api/contractors/999999/addresses");
        unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // -----------------------------------------------------------------
    // 6. Contract addresses can be created (previously impossible) and are
    //    tenant-scoped to the contract's tenant
    // -----------------------------------------------------------------

    [Fact]
    public async Task Address_Contract_CreatesAndIsCrossTenantInvisible()
    {
        var token1 = await GetTokenAsync("admin@qontract.com");
        Authenticate(token1);

        var createContract = await _client.PostAsJsonAsync("/api/contracts", new
        {
            title = $"TI Contract {Guid.NewGuid():N}",
            status = "Active"
        });
        createContract.StatusCode.Should().Be(HttpStatusCode.Created);
        var contract = await createContract.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var contractId = contract.GetProperty("id").GetInt32();

        // Contract address for the caller's own contract: OK.
        var ok = await _client.PostAsJsonAsync("/api/addresses", new
        {
            entityType = "Contract",
            contractId = contractId,
            street = "Site 1",
            city = "Berlin",
            zip = "10115",
            country = "Germany"
        });
        ok.StatusCode.Should().Be(HttpStatusCode.Created);
        var address = await ok.Content.ReadFromJsonAsync<AddressDTO>(JsonOptions);
        address.Should().NotBeNull();
        address!.EntityType.Should().Be(Qontract.Domain.Models.EntityType.Contract);

        // Tenant 2 cannot see or create on tenant 1's contract.
        var token2 = await GetTokenAsync("admin2@qontract.com");
        Authenticate(token2);

        var get2 = await _client.GetAsync($"/api/addresses/{address.Id}");
        get2.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var foreign = await _client.PostAsJsonAsync("/api/addresses", new
        {
            entityType = "Contract",
            contractId = contractId, // tenant 1's contract
            street = "Intrusion 2",
            city = "Berlin",
            zip = "10115",
            country = "Germany"
        });
        foreign.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
