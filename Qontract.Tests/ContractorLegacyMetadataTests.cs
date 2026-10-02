using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Qontract.Application.Models;
using Xunit;

namespace Qontract.Tests;

public class ContractorLegacyMetadataTests : IClassFixture<QontractWebApplicationFactory>
{
    private readonly QontractWebApplicationFactory factory;
    public ContractorLegacyMetadataTests(QontractWebApplicationFactory factory) => this.factory = factory;

    private async Task<HttpClient> Client(string email = "admin@qontract.com")
    {
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "password123" });
        var auth = (await login.Content.ReadFromJsonAsync<AuthDTO>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        return client;
    }

    [Theory]
    [InlineData("Company")][InlineData("Individual")]
    public async Task MetadataRoundTripsWithoutTruncation_AndTenantIsolationAndClearingArePreserved(string type)
    {
        using var client = await Client();
        const string website = " www.legacy.example/path ";
        var description = " Original description\n" + new string('d', 3000);
        var notes = " First line — ÄÖÜ\n" + new string('n', 12000) + "\n ";
        var response = await client.PostAsJsonAsync("/api/contractors", new
        { name = "Legacy-" + Guid.NewGuid(), type, website, description, notes });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = (await response.Content.ReadFromJsonAsync<ContractorDTO>())!;
        created.Website.Should().Be(website); created.Description.Should().Be(description); created.Notes.Should().Be(notes);
        var list = (await client.GetFromJsonAsync<List<ContractorDTO>>("/api/contractors"))!;
        list.Single(c => c.Id == created.Id).Notes.Should().Be(notes);
        list.Single(c => c.Id == created.Id).Website.Should().Be(website);
        var renamed = await client.PutAsJsonAsync($"/api/contractors/{created.Id}", new { name = "Renamed", website = (string?)null, description = (string?)null, notes = (string?)null });
        renamed.StatusCode.Should().Be(HttpStatusCode.OK);
        var unchanged = (await renamed.Content.ReadFromJsonAsync<ContractorDTO>())!;
        unchanged.Website.Should().Be(website); unchanged.Description.Should().Be(description); unchanged.Notes.Should().Be(notes);
        foreach (var action in new[] { "deactivate", "activate" })
        {
            var changed = await client.PatchAsync($"/api/contractors/{created.Id}/{action}", null);
            changed.StatusCode.Should().Be(HttpStatusCode.OK);
            var fresh = (await changed.Content.ReadFromJsonAsync<ContractorDTO>())!;
            fresh.Notes.Should().Be(notes); fresh.Description.Should().Be(description); fresh.Website.Should().Be(website);
        }
        using var foreign = await Client("admin2@qontract.com");
        (await foreign.GetAsync($"/api/contractors/{created.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await foreign.PutAsJsonAsync($"/api/contractors/{created.Id}", new { notes = "Wrong tenant" })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var edited = await client.PutAsJsonAsync($"/api/contractors/{created.Id}", new { website = "https://example.com", description = "New description", notes = "Updated\nnotes" });
        edited.StatusCode.Should().Be(HttpStatusCode.OK);
        var detail = (await client.GetFromJsonAsync<ContractorDTO>($"/api/contractors/{created.Id}"))!;
        detail.Website.Should().Be("https://example.com"); detail.Description.Should().Be("New description"); detail.Notes.Should().Be("Updated\nnotes");
        var cleared = await client.PutAsJsonAsync($"/api/contractors/{created.Id}", new { website = "", description = "", notes = "" });
        cleared.StatusCode.Should().Be(HttpStatusCode.OK);
        var empty = (await cleared.Content.ReadFromJsonAsync<ContractorDTO>())!;
        empty.Website.Should().BeEmpty(); empty.Description.Should().BeEmpty(); empty.Notes.Should().BeEmpty();
    }

    [Fact]
    public async Task OlderClientsCanCreateWithoutMetadata()
    {
        using var client = await Client();
        var response = await client.PostAsJsonAsync("/api/contractors", new { name = "Older client" });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var dto = (await response.Content.ReadFromJsonAsync<ContractorDTO>())!;
        dto.Website.Should().BeNull(); dto.Description.Should().BeNull(); dto.Notes.Should().BeNull();
    }
}
