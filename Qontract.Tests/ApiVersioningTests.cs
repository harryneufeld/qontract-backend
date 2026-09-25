using FluentAssertions;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Xunit;

namespace Qontract.Tests;

/// <summary>
/// End-to-end tests for API versioning (issue #28). The current API surface
/// is version 1.0 and must be reachable four ways: the legacy unversioned
/// route (what the existing Flutter app + every other integration test use),
/// the URL-segment route (/api/v1/...), the query string (?api-version=1.0)
/// and the X-Api-Version header. Introducing versioning must be fully backward
/// compatible, so the unversioned route keeps working and resolves to the
/// default version. Tests within this class run sequentially.
/// </summary>
public class ApiVersioningTests : IClassFixture<QontractWebApplicationFactory>
{
    private readonly HttpClient _client;

    public ApiVersioningTests(QontractWebApplicationFactory factory)
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

    private void Authenticate(string token)
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    [Fact]
    public async Task UnversionedRoute_ShouldStillWork_AndResolveToDefaultVersion()
    {
        // Backward compatibility: the existing Flutter app calls /api/contracts
        // with no version. It must keep resolving (to the default, v1.0).
        var token = await GetTokenAsync("admin@qontract.com");
        Authenticate(token);

        var response = await _client.GetAsync("/api/contracts");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task UnversionedRoute_ShouldReportSupportedVersions()
    {
        // ReportApiVersions advertises the available versions so clients can
        // discover what the API offers.
        var token = await GetTokenAsync("admin@qontract.com");
        Authenticate(token);

        var response = await _client.GetAsync("/api/contracts");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.TryGetValues("api-supported-versions", out var versions)
            .Should().BeTrue("versioned responses report the supported API versions");
        versions.Should().ContainSingle().Which.Should().Be("1.0");
    }

    [Fact]
    public async Task UrlSegmentRoute_ShouldResolveToVersion1()
    {
        var token = await GetTokenAsync("admin@qontract.com");
        Authenticate(token);

        var response = await _client.GetAsync("/api/v1/contracts");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task QueryStringVersion_ShouldResolveToVersion1()
    {
        var token = await GetTokenAsync("admin@qontract.com");
        Authenticate(token);

        var response = await _client.GetAsync("/api/contracts?api-version=1.0");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task HeaderVersion_ShouldResolveToVersion1()
    {
        var token = await GetTokenAsync("admin@qontract.com");
        Authenticate(token);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/contracts");
        request.Headers.Add("X-Api-Version", "1.0");
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task VersionedRoute_ShouldReturnSameData_AsUnversionedRoute()
    {
        // Both resolve to the same v1.0 controller, so the payload must be
        // identical regardless of how the (default) version is expressed.
        var token = await GetTokenAsync("admin@qontract.com");
        Authenticate(token);

        var unversioned = await _client.GetStringAsync("/api/contracts");
        var versioned = await _client.GetStringAsync("/api/v1/contracts");

        versioned.Should().Be(unversioned);
    }

    [Fact]
    public async Task VersionedRoute_ShouldRequireAuth_LikeUnversioned()
    {
        // The versioned route is just another entry point to the same
        // [Authorize]d controller: no token -> 401 (route exists, auth required).
        _client.DefaultRequestHeaders.Authorization = null;

        var response = await _client.GetAsync("/api/v1/contracts");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task UnknownVersion_ShouldNotResolve()
    {
        // Only v1.0 is defined today. Asking for v2 must not silently fall
        // back to v1 — it is an unknown version and 404s.
        var token = await GetTokenAsync("admin@qontract.com");
        Authenticate(token);

        var response = await _client.GetAsync("/api/v2/contracts");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task SwaggerDocument_ShouldBeServedForVersion1()
    {
        // Development-only Swagger: the versioned API explorer publishes a
        // document per API version. v1.0's document must generate cleanly.
        var response = await _client.GetAsync("/swagger/v1/swagger.json");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        json.Should().Contain("\"openapi\"");
    }
}
