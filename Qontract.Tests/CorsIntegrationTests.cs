using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Xunit;

namespace Qontract.Tests;

/// <summary>
/// CORS behaviour (issue #12). The Flutter web app is served from a
/// localhost origin that is NOT hardcoded (flutter run picks a random
/// high port, e.g. 5354), so in Development the API must accept any
/// origin. The factory pins the environment to Development, so these
/// tests exercise the permissive branch.
/// </summary>
public class CorsIntegrationTests : IClassFixture<QontractWebApplicationFactory>
{
    private readonly HttpClient _client;

    public CorsIntegrationTests(QontractWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    private static HttpRequestMessage Preflight(string origin, string method = "POST")
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "/api/auth/login");
        request.Headers.TryAddWithoutValidation("Origin", origin);
        request.Headers.TryAddWithoutValidation("Access-Control-Request-Method", method);
        request.Headers.TryAddWithoutValidation("Access-Control-Request-Headers", "authorization,content-type");
        return request;
    }

    [Fact]
    public async Task Preflight_FromArbitraryLocalhostPort_ShouldBeAllowed()
    {
        // The exact origin from the reported bug.
        var response = await _client.SendAsync(Preflight("http://localhost:5354"));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        // A browser needs this header to actually permit the request.
        response.Headers.Should().Contain(h => h.Key == "Access-Control-Allow-Origin");
        response.Headers.GetValues("Access-Control-Allow-Origin")
            .Should().NotBeEmpty()
            .And.OnlyContain(v => !string.IsNullOrWhiteSpace(v));
    }

    [Fact]
    public async Task Preflight_ShouldEchoTheRequestedMethod()
    {
        var response = await _client.SendAsync(Preflight("http://localhost:8123", "POST"));

        response.Headers.GetValues("Access-Control-Allow-Origin").Should().NotBeEmpty();
        // The framework echoes the requested method back for a
        // well-formed preflight — that is what the browser needs.
        var allowMethods = response.Headers.GetValues("Access-Control-Allow-Methods")
            .SelectMany(m => m.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        allowMethods.Should().Contain(m => m.Equals("POST", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SimpleCrossOriginRequest_ShouldCarryAllowOriginHeader()
    {
        // An unauthenticated GET still goes through the CORS middleware
        // and must answer with an allow-origin header for the browser to
        // expose the response.
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/contracts");
        request.Headers.TryAddWithoutValidation("Origin", "http://localhost:5354");

        var response = await _client.SendAsync(request);

        // 401 (no token) is expected, but the CORS header must be present
        // so the browser can surface the error to the app.
        response.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.OK);
        response.Headers.Should().Contain(h => h.Key == "Access-Control-Allow-Origin");
    }
}
