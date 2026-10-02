using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Qontract.Application.Models;
using Xunit;

namespace Qontract.Tests;

public class ContractLegacyMetadataTests : IClassFixture<QontractWebApplicationFactory>
{
    private readonly QontractWebApplicationFactory factory;
    private static readonly JsonSerializerOptions JsonOptions = new()
    { PropertyNameCaseInsensitive = true, Converters = { new JsonStringEnumConverter() } };
    public ContractLegacyMetadataTests(QontractWebApplicationFactory factory) => this.factory = factory;

    private async Task<HttpClient> Client(string email = "admin@qontract.com")
    {
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "password123" });
        var auth = (await login.Content.ReadFromJsonAsync<AuthDTO>(JsonOptions))!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        return client;
    }

    [Theory]
    [InlineData("None")][InlineData("Insurance")][InlineData("Loan")]
    [InlineData("Subscription")][InlineData("Other")]
    public async Task LegacyFieldsRoundTrip_AndMetadataChangesDoNotCreatePriceRevisions(string type)
    {
        using var client = await Client();
        const string iban = " DE89 3704 0044 0532 0130 00 ";
        const string notes = " First line\nSecond line — ÄÖÜ\n ";
        var response = await client.PostAsJsonAsync("/api/contracts", new
        { title = "Metadata-" + Guid.NewGuid(), iban, notes, type, value = "100", billingPeriod = "Monthly", status = "Active" });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = (await response.Content.ReadFromJsonAsync<ContractDTO>(JsonOptions))!;
        created.Iban.Should().Be(iban); created.Notes.Should().Be(notes); created.Type.ToString().Should().Be(type);
        created.Status.Should().Be(Qontract.Domain.Models.ContractStatus.Active);
        var list = (await client.GetFromJsonAsync<List<ContractDTO>>("/api/contracts", JsonOptions))!;
        list.Single(c => c.Id == created.Id).Notes.Should().Be(notes);
        list.Single(c => c.Id == created.Id).Type.ToString().Should().Be(type);
        var renamed = await client.PutAsJsonAsync($"/api/contracts/{created.Id}", new { title = "Renamed", iban = (string?)null, notes = (string?)null, type = (string?)null });
        renamed.StatusCode.Should().Be(HttpStatusCode.OK);
        var unchanged = (await renamed.Content.ReadFromJsonAsync<ContractDTO>(JsonOptions))!;
        unchanged.Iban.Should().Be(iban); unchanged.Notes.Should().Be(notes); unchanged.Type.Should().Be(created.Type);
        var changed = await client.PutAsJsonAsync($"/api/contracts/{created.Id}", new { notes = "New\ncontract note", iban = "raw historical bank reference", type = "Other" });
        changed.StatusCode.Should().Be(HttpStatusCode.OK);
        var fresh = (await client.GetFromJsonAsync<ContractDTO>($"/api/contracts/{created.Id}", JsonOptions))!;
        fresh.Notes.Should().Be("New\ncontract note"); fresh.Iban.Should().Be("raw historical bank reference");
        fresh.PriceHistory.Should().ContainSingle(); fresh.PriceHistory[0].Notes.Should().BeNull();
        using var foreign = await Client("admin2@qontract.com");
        (await foreign.GetAsync($"/api/contracts/{created.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await foreign.PutAsJsonAsync($"/api/contracts/{created.Id}", new { notes = "Wrong tenant" })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var cleared = await client.PutAsJsonAsync($"/api/contracts/{created.Id}", new { iban = "", notes = "", clearType = true });
        cleared.StatusCode.Should().Be(HttpStatusCode.OK);
        var empty = (await cleared.Content.ReadFromJsonAsync<ContractDTO>(JsonOptions))!;
        empty.Iban.Should().BeEmpty(); empty.Notes.Should().BeEmpty(); empty.Type.Should().BeNull();
        empty.PriceHistory.Should().ContainSingle();
    }

    [Fact]
    public async Task LongLegacyNotesAndBankReferencesSurviveDeactivationWithoutTruncation()
    {
        using var client = await Client();
        var notes = new string('x', 12000) + "\nHistoric note";
        var iban = new string('y', 200);
        var response = await client.PostAsJsonAsync("/api/contracts", new { title = "Historic", notes, iban, type = "Loan" });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = (await response.Content.ReadFromJsonAsync<ContractDTO>(JsonOptions))!;
        var deactivated = await client.PatchAsync($"/api/contracts/{created.Id}/deactivate", null);
        deactivated.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = (await deactivated.Content.ReadFromJsonAsync<ContractDTO>(JsonOptions))!;
        dto.Notes.Should().Be(notes); dto.Iban.Should().Be(iban); dto.Type.Should().Be(created.Type);
    }

    [Theory]
    [InlineData("NotAType")][InlineData("999")]
    public async Task InvalidTypesRejectedOnCreateAndUpdate(string type)
    {
        using var client = await Client();
        (await client.PostAsJsonAsync("/api/contracts", new { title = "Invalid", type })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var response = await client.PostAsJsonAsync("/api/contracts", new { title = "Unclassified" });
        var created = (await response.Content.ReadFromJsonAsync<ContractDTO>(JsonOptions))!;
        created.Type.Should().BeNull();
        (await client.PutAsJsonAsync($"/api/contracts/{created.Id}", new { type })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.PutAsJsonAsync($"/api/contracts/{created.Id}", new { type = "Other", clearType = true })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
