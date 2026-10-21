using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Qontract.Application.Models;
using Qontract.Infrastructure.Data;

namespace Qontract.Tests;

public class TermsAcceptanceTests : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private const string Version = "2026-10";
    private const string Url = "https://qontract.example/terms";
    private readonly QontractWebApplicationFactory _root = new();
    private readonly Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> _host;
    private readonly HttpClient _client;

    public TermsAcceptanceTests()
    {
        _host = WithTerms(_root, Version);
        _client = _host.CreateClient();
    }

    public void Dispose() { _client.Dispose(); _host.Dispose(); _root.Dispose(); }

    private static Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> WithTerms(
        Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> factory, string version) =>
        factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Registration:AllowPublic", "true");
            b.UseSetting("Terms:Version", version);
            b.UseSetting("Terms:Url", Url);
        });

    private static Task<HttpResponseMessage> Register(HttpClient client, string email, string? acceptedTermsVersion) =>
        client.PostAsJsonAsync("/api/auth/register", new
        { name = "Terms user", email, password = "terms-password-123", tenantName = "Terms family", acceptedTermsVersion });

    [Fact]
    public async Task Policy_ExposesTheCurrentTerms()
    {
        var policy = await _client.GetFromJsonAsync<JsonElement>("/api/auth/registration");
        policy.GetProperty("termsVersion").GetString().Should().Be(Version);
        policy.GetProperty("termsUrl").GetString().Should().Be(Url);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("2025-01")]
    public async Task Registration_WithoutTheCurrentVersion_IsRejected_WithoutCreatingAnAccount(string? accepted)
    {
        var email = $"terms-{Guid.NewGuid():N}@example.com";
        (await Register(_client, email, accepted)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var scope = _host.Services.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Users.AnyAsync(u => u.Email == email)).Should().BeFalse();
    }

    [Fact]
    public async Task Registration_RecordsAcceptance_AndChangedTermsRequireAcceptingAgain()
    {
        var email = $"terms-{Guid.NewGuid():N}@example.com";
        var response = await Register(_client, email, Version);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var auth = (await response.Content.ReadFromJsonAsync<AuthDTO>(JsonOptions))!;
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        var status = await _client.GetFromJsonAsync<JsonElement>("/api/auth/terms");
        status.GetProperty("accepted").GetBoolean().Should().BeTrue();

        using var updated = WithTerms(_root, "2027-01");
        using var client = updated.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        status = await client.GetFromJsonAsync<JsonElement>("/api/auth/terms");
        status.GetProperty("version").GetString().Should().Be("2027-01");
        status.GetProperty("accepted").GetBoolean().Should().BeFalse();

        (await client.PostAsJsonAsync("/api/auth/terms/accept", new { version = Version })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.PostAsJsonAsync("/api/auth/terms/accept", new { version = "2027-01" })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PostAsJsonAsync("/api/auth/terms/accept", new { version = "2027-01" })).StatusCode.Should().Be(HttpStatusCode.OK);
        status = await client.GetFromJsonAsync<JsonElement>("/api/auth/terms");
        status.GetProperty("accepted").GetBoolean().Should().BeTrue();

        using var scope = updated.Services.CreateScope();
        var versions = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().TermsAcceptances
            .Where(a => a.UserId == auth.UserId).Select(a => a.Version).ToListAsync();
        versions.Should().BeEquivalentTo([Version, "2027-01"]);
    }

    [Fact]
    public async Task WithoutConfiguredTerms_NothingIsRequiredOrRecorded()
    {
        using var factory = new QontractWebApplicationFactory().WithWebHostBuilder(b => b.UseSetting("Registration:AllowPublic", "true"));
        using var client = factory.CreateClient();
        var policy = await client.GetFromJsonAsync<JsonElement>("/api/auth/registration");
        policy.GetProperty("termsVersion").ValueKind.Should().Be(JsonValueKind.Null);
        (await Register(client, $"noterms-{Guid.NewGuid():N}@example.com", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        using var scope = factory.Services.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().TermsAcceptances.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public void TermsVersion_WithoutHttpsUrl_FailsAtStartup()
    {
        using var factory = new QontractWebApplicationFactory().WithWebHostBuilder(b =>
        {
            b.UseSetting("Terms:Version", Version);
            b.UseSetting("Terms:Url", "http://qontract.example/terms");
        });
        Assert.ThrowsAny<Exception>(() => factory.CreateClient());
    }
}
