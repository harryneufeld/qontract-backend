using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Xunit;
using Qontract.Application.Models;

namespace Qontract.Tests;

public class BillingPeriodTests : IClassFixture<QontractWebApplicationFactory>
{
    private readonly QontractWebApplicationFactory factory;
    private static readonly JsonSerializerOptions JsonOptions = new()
    { PropertyNameCaseInsensitive = true, Converters = { new JsonStringEnumConverter() } };
    public BillingPeriodTests(QontractWebApplicationFactory factory) => this.factory = factory;
    private async Task<HttpClient> Client(string email = "admin@qontract.com")
    {
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "password123" });
        var auth = (await login.Content.ReadFromJsonAsync<AuthDTO>(JsonOptions))!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        return client;
    }
    private static async Task<ContractDTO> Create(HttpClient client, string? period = "Monthly", string value = "100,50")
    {
        var response = await client.PostAsJsonAsync("/api/contracts", new
        { title = "Billing-" + Guid.NewGuid(), value, billingPeriod = period, currency = "EUR", startDate = "2026-01-01", status = "Active" });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<ContractDTO>(JsonOptions))!;
    }

    [Theory]
    [InlineData("OneTime")][InlineData("Daily")][InlineData("Weekly")]
    [InlineData("Monthly")][InlineData("Quarterly")][InlineData("Yearly")]
    public async Task PeriodsAndInitialHistory_RoundTripThroughListAndDetail(string period)
    {
        using var client = await Client();
        var created = await Create(client, period);
        created.Value.Should().Be("100.50");
        created.BillingPeriod.ToString().Should().Be(period);
        created.PriceHistory.Should().ContainSingle();
        created.PriceHistory[0].EffectiveFrom.Should().Be("2026-01-01");
        var fresh = (await client.GetFromJsonAsync<ContractDTO>($"/api/contracts/{created.Id}", JsonOptions))!;
        fresh.PriceHistory.Should().ContainSingle();
        fresh.PriceHistory[0].BillingPeriod.ToString().Should().Be(period);
        var list = (await client.GetFromJsonAsync<List<ContractDTO>>("/api/contracts", JsonOptions))!;
        list.Single(c => c.Id == created.Id).PriceHistory.Should().ContainSingle();
        using var foreign = await Client("admin2@qontract.com");
        (await foreign.GetAsync($"/api/contracts/{created.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ChangesPreserveOldAmountsCurrenciesPeriodsAndNotes_WithoutOverlappingIntervals()
    {
        using var client = await Client();
        var created = await Create(client);
        var response = await client.PutAsJsonAsync($"/api/contracts/{created.Id}", new
        { value = "120", currency = "USD", billingPeriod = "Quarterly", priceEffectiveFrom = "2026-07-01", priceNotes = "New agreement" });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var changed = (await response.Content.ReadFromJsonAsync<ContractDTO>(JsonOptions))!;
        changed.PriceHistory.Should().HaveCount(2);
        changed.PriceHistory[0].Value.Should().Be("100.50");
        changed.PriceHistory[0].Currency.Should().Be("EUR");
        changed.PriceHistory[0].EffectiveUntil.Should().Be("2026-07-01");
        changed.PriceHistory[1].Notes.Should().Be("New agreement");
        // A correction on the same effective day retains the superseded snapshot.
        (await client.PutAsJsonAsync($"/api/contracts/{created.Id}", new { value = "130", priceEffectiveFrom = "2026-07-01" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PutAsJsonAsync($"/api/contracts/{created.Id}", new { title = "Renamed" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PutAsJsonAsync($"/api/contracts/{created.Id}", new { value = "1", priceEffectiveFrom = "2026-06-30" }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var final = (await client.GetFromJsonAsync<ContractDTO>($"/api/contracts/{created.Id}", JsonOptions))!;
        final.Value.Should().Be("130");
        final.PriceHistory.Should().HaveCount(3);
        final.PriceHistory.Count(p => p.EffectiveUntil == null).Should().Be(1);
        final.PriceHistory[1].Value.Should().Be("120");
    }

    [Theory]
    [InlineData("-1")][InlineData("NaN")][InlineData("100 EUR")][InlineData("1.234,56")]
    public async Task ExplicitBillingRejectsInvalidAmounts(string value)
    {
        using var client = await Client();
        (await client.PostAsJsonAsync("/api/contracts", new { title = "Invalid", value, billingPeriod = "Monthly" }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task NoteOnlyAndFutureRevisionsAreRetained()
    {
        using var client = await Client();
        var created = await Create(client);
        var note = await client.PutAsJsonAsync($"/api/contracts/{created.Id}", new
        { priceNotes = "Reviewed unchanged price", priceEffectiveFrom = "2026-07-01" });
        note.StatusCode.Should().Be(HttpStatusCode.OK);
        var noted = (await note.Content.ReadFromJsonAsync<ContractDTO>(JsonOptions))!;
        noted.PriceHistory.Should().HaveCount(2);
        noted.PriceHistory[1].Notes.Should().Be("Reviewed unchanged price");
        var future = await client.PutAsJsonAsync($"/api/contracts/{created.Id}", new
        { value = "200", priceEffectiveFrom = "2030-01-01" });
        future.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = (await future.Content.ReadFromJsonAsync<ContractDTO>(JsonOptions))!;
        dto.PriceHistory.Should().HaveCount(3);
        dto.PriceHistory.Last().EffectiveFrom.Should().Be("2030-01-01");
        dto.PriceHistory[1].EffectiveUntil.Should().Be("2030-01-01");
    }

    [Fact]
    public async Task UnclassifiedLegacyValuesArePreserved_AndOmittedPeriodOnUpdateRetainsKnownPeriod()
    {
        using var client = await Client();
        var unknown = await Create(client, null, "1.234,56 EUR");
        unknown.BillingPeriod.Should().BeNull();
        unknown.Value.Should().Be("1.234,56 EUR");
        var known = await Create(client);
        var updated = await client.PutAsJsonAsync($"/api/contracts/{known.Id}", new { value = "200" });
        var dto = (await updated.Content.ReadFromJsonAsync<ContractDTO>(JsonOptions))!;
        dto.BillingPeriod.Should().Be(Qontract.Domain.Models.BillingPeriod.Monthly);
        dto.PriceHistory.Should().HaveCount(2);
    }
}
