using FluentAssertions;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Xunit;

namespace Qontract.Tests;

/// <summary>
/// End-to-end tests for the dashboard status filter on the contract list
/// (app#36): GET /api/contracts?status=active|expiring|expired|minDate
/// must return ALL of the tenant's contracts in that bucket (not just the
/// loaded page) with the same bucket rules as /api/contracts/stats, and
/// compose with offset paging.
///
/// One scenario [Fact] keeps the assertions absolute against the shared
/// in-memory DB (seed data contains no contracts).
/// </summary>
public class ContractStatusFilterTests : IClassFixture<QontractWebApplicationFactory>
{
    private readonly HttpClient _client;

    public ContractStatusFilterTests(QontractWebApplicationFactory factory)
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

    private record ContractJson(int Id, string Title, string Status);

    private static string Fmt(DateOnly d) => d.ToString("yyyy-MM-dd");

    private async Task<ContractJson?> CreateContractAsync(
        string title, string status,
        string? endDate = null, string? minDate = null)
    {
        var response = await _client.PostAsJsonAsync("/api/contracts", new
        {
            title,
            status,
            endDate,
            minimumContractDate = minDate
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return await response.Content.ReadFromJsonAsync<ContractJson>();
    }

    private async Task<List<ContractJson>> GetListAsync(string query)
    {
        var response = await _client.GetAsync($"/api/contracts{query}");
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "status={0}: {1}", query, await response.Content.ReadAsStringAsync());
        var list = await response.Content.ReadFromJsonAsync<List<ContractJson>>();
        list.Should().NotBeNull();
        return list!;
    }

    [Fact]
    public async Task StatusFilter_ShouldReturnAllTenantContractsInBucket()
    {
        var token = await GetTokenAsync("admin@qontract.com");
        Authenticate(token);

        var today = DateOnly.FromDateTime(DateTime.Now);

        var activeFar = await CreateContractAsync("F active far", "Active",
            endDate: Fmt(today.AddDays(200)));
        var expiring = await CreateContractAsync("F expiring", "Active",
            endDate: Fmt(today.AddDays(10)));
        var expiredDate = await CreateContractAsync("F expired date", "Active",
            endDate: Fmt(today.AddDays(-10)));
        var statusExpired = await CreateContractAsync("F status expired", "Expired",
            endDate: Fmt(today.AddDays(100)));
        var draft = await CreateContractAsync("F draft", "Draft",
            endDate: Fmt(today.AddDays(200)));
        var minDateNear = await CreateContractAsync("F minDate near", "Active",
            endDate: Fmt(today.AddDays(200)), minDate: Fmt(today.AddDays(30)));
        var minDateFar = await CreateContractAsync("F minDate far", "Active",
            endDate: Fmt(today.AddDays(400)), minDate: Fmt(today.AddDays(120)));
        var minDatePast = await CreateContractAsync("F minDate past", "Active",
            endDate: Fmt(today.AddDays(400)), minDate: Fmt(today.AddDays(-5)));

        // ---- no filter: historic behaviour unchanged (all 8 rows) ----
        (await GetListAsync("")).Should().HaveCount(8);

        // ---- active: 6 rows, every one with status Active -------------
        var active = await GetListAsync("?status=active");
        active.Should().HaveCount(6);
        active.Should().OnlyContain(c => c.Status == "Active");
        active.Select(c => c.Id).Should().NotContain(draft!.Id);

        // ---- expiring: exactly the 10-days-out contract ---------------
        var expiringList = await GetListAsync("?status=expiring");
        expiringList.Should().BeEquivalentTo(new[] { expiring! });

        // ---- expired: date-driven + status-driven ----------------------
        var expiredList = await GetListAsync("?status=expired");
        expiredList.Select(c => c.Id)
            .Should().BeEquivalentTo(new[] { expiredDate!.Id, statusExpired!.Id });

        // ---- minDate: near + past, most urgent first -------------------
        var minDateList = await GetListAsync("?status=minDate");
        minDateList.Select(c => c.Id)
            .Should().BeEquivalentTo(new[] { minDateNear!.Id, minDatePast!.Id });
        minDateList.Should().HaveCount(2);
        minDateList[0].Id.Should().Be(minDatePast!.Id);   // -5 days first
        minDateList[1].Id.Should().Be(minDateNear!.Id);   // +30 days second
        minDateList.Should().NotContain(c => c.Id == minDateFar!.Id);

        // ---- composes with paging: 3+3+0 pages of the 6 active --------
        var page1 = await GetListAsync("?status=active&page=1&pageSize=3");
        var page2 = await GetListAsync("?status=active&page=2&pageSize=3");
        var page3 = await GetListAsync("?status=active&page=3&pageSize=3");
        page1.Should().HaveCount(3);
        page2.Should().HaveCount(3);
        page3.Should().BeEmpty();
        page1.Select(c => c.Id).Concat(page2.Select(c => c.Id)).ToList()
            .Distinct().Should().HaveCount(6);
        (page1.Select(c => c.Id).Intersect(page2.Select(c => c.Id)))
            .Should().BeEmpty();

        // ---- unknown bucket: 400 ---------------------------------------
        var bad = await _client.GetAsync("/api/contracts?status=bogus");
        bad.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
