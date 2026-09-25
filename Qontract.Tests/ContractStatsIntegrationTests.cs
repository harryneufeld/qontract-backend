using FluentAssertions;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Xunit;

namespace Qontract.Tests;

/// <summary>
/// End-to-end tests for the tenant-wide dashboard counters (app#33):
/// GET /api/contracts/stats and the per-contractor contractCount on
/// GET /api/contractors. Both must reflect ALL of the tenant's contracts,
/// not just the first (paginated) page.
///
/// One scenario [Fact] keeps the assertions absolute against the shared
/// in-memory DB (seed data contains no contracts or contractors).
/// </summary>
public class ContractStatsIntegrationTests : IClassFixture<QontractWebApplicationFactory>
{
    private readonly HttpClient _client;

    public ContractStatsIntegrationTests(QontractWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    private async Task<string> GetTokenAsync(string email)
    {
        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", new { email, password = "password123" });
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var auth = await loginResponse.Content.ReadFromJsonAsync<Qontract.Application.Models.AuthDTO>();
        auth.Should().NotBeNull();
        return auth!.AccessToken;
    }

    private void Authenticate(string? token)
    {
        _client.DefaultRequestHeaders.Authorization =
            token == null ? null : new AuthenticationHeaderValue("Bearer", token);
    }

    private record ContractorJson(int Id, string Name, int ContractCount);
    private record ContractJson(int Id, string Title, string Status, int? ContractorId);
    private record StatsJson(int Total, int Active, int Expiring, int Expired, int MinDateApproaching);

    private static string Fmt(DateOnly d) => d.ToString("yyyy-MM-dd");

    [Fact]
    public async Task GetStats_WithoutToken_ShouldReturn401()
    {
        Authenticate(null);
        var response = await _client.GetAsync("/api/contracts/stats");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task StatsAndContractCount_ShouldReflectAllTenantContracts()
    {
        var token = await GetTokenAsync("admin@qontract.com");
        Authenticate(token);

        var today = DateOnly.FromDateTime(DateTime.Now);

        // Two contractors: A gets the contracts below, B stays at zero.
        var contractorA = (await CreateContractorAsync($"Stats A {Guid.NewGuid():N}"))!;
        var contractorB = (await CreateContractorAsync($"Stats B {Guid.NewGuid():N}"))!;

        // A: 6 contracts covering every bucket.
        //  1. Active, end date passed        -> expired (date-driven)
        //  2. Active, ends in 10 days        -> active + expiring
        //  3. Active, ends in 200 days       -> active only
        //  4. Active, min date in 30 days    -> active + minDateApproaching
        //  5. Status Expired, future end     -> expired (status-driven)
        //  6. Draft, end date passed         -> expired (date-driven)
        await CreateContractAsync("Stats expired date", "Active",
            endDate: Fmt(today.AddDays(-10)), contractorId: contractorA.Id);
        await CreateContractAsync("Stats expiring", "Active",
            endDate: Fmt(today.AddDays(10)), contractorId: contractorA.Id);
        await CreateContractAsync("Stats active far", "Active",
            endDate: Fmt(today.AddDays(200)), contractorId: contractorA.Id);
        await CreateContractAsync("Stats min date near", "Active",
            endDate: Fmt(today.AddDays(200)), minDate: Fmt(today.AddDays(30)),
            contractorId: contractorA.Id);
        await CreateContractAsync("Stats status expired", "Expired",
            endDate: Fmt(today.AddDays(100)), contractorId: contractorA.Id);
        await CreateContractAsync("Stats draft expired", "Draft",
            endDate: Fmt(today.AddDays(-5)), contractorId: contractorA.Id);

        // One unlinked contract (no contractor) — still counted in stats,
        // but in nobody's per-contractor count.
        await CreateContractAsync("Stats unlinked", "Active",
            endDate: Fmt(today.AddDays(50)));

        // ---- stats endpoint: all 7 contracts of the tenant ------------
        var statsResponse = await _client.GetAsync("/api/contracts/stats");
        statsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var stats = await statsResponse.Content.ReadFromJsonAsync<StatsJson>();
        stats.Should().NotBeNull();
        stats!.Total.Should().Be(7);
        stats.Active.Should().Be(5);      // 1, 2, 3, 4 + the unlinked one
        stats.Expiring.Should().Be(1);    // 2 (10 days out, not yet expired)
        stats.Expired.Should().Be(3);     // 1 (date), 5 (status), 6 (date)
        stats.MinDateApproaching.Should().Be(1); // 4 (30 days <= 90)

        // ---- contractors list: server-side per-contractor totals ------
        var contractorsResponse = await _client.GetAsync("/api/contractors");
        contractorsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var contractors = await contractorsResponse.Content.ReadFromJsonAsync<List<ContractorJson>>();
        contractors.Should().NotBeNull();
        contractors!.Should().NotBeEmpty();

        var a = contractors.First(c => c.Id == contractorA.Id);
        var b = contractors.First(c => c.Id == contractorB.Id);
        a.ContractCount.Should().Be(6);
        b.ContractCount.Should().Be(0);
        // No contractor claims the unlinked contract.
        contractors.Sum(c => c.ContractCount).Should().Be(6);

        // ---- single-contractor endpoint carries the same count --------
        var singleResponse = await _client.GetAsync($"/api/contractors/{contractorA.Id}");
        singleResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var single = await singleResponse.Content.ReadFromJsonAsync<ContractorJson>();
        single.Should().NotBeNull();
        single!.ContractCount.Should().Be(6);

        // ---- soft-deleted contracts leave both counters ---------------
        var toDelete = await CreateContractAsync("Stats to delete", "Active",
            endDate: Fmt(today.AddDays(10)), contractorId: contractorA.Id);
        var deleteResponse = await _client.DeleteAsync($"/api/contracts/{toDelete!.Id}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var statsAfter = await (await _client.GetAsync("/api/contracts/stats"))
            .Content.ReadFromJsonAsync<StatsJson>();
        statsAfter!.Total.Should().Be(7); // 8 created, 1 deleted
        statsAfter.Expired.Should().Be(3);

        var contractorsAfter = await (await _client.GetAsync("/api/contractors"))
            .Content.ReadFromJsonAsync<List<ContractorJson>>();
        contractorsAfter!.First(c => c.Id == contractorA.Id).ContractCount.Should().Be(6);
    }

    private async Task<ContractorJson?> CreateContractorAsync(string name)
    {
        var response = await _client.PostAsJsonAsync("/api/contractors", new
        {
            name,
            type = "Company"
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return await response.Content.ReadFromJsonAsync<ContractorJson>();
    }

    private async Task<ContractJson?> CreateContractAsync(
        string title, string status,
        string? endDate = null, string? minDate = null, int? contractorId = null)
    {
        var response = await _client.PostAsJsonAsync("/api/contracts", new
        {
            title,
            status,
            endDate,
            minimumContractDate = minDate,
            contractorId
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return await response.Content.ReadFromJsonAsync<ContractJson>();
    }
}
