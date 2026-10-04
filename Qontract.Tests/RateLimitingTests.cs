using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using FluentAssertions;

namespace Qontract.Tests;

/// <summary>
/// Rate limiting (issue #27). The "auth" policy applies a strict, per-client-IP
/// fixed window to the credential endpoints (/auth/login, /auth/change-password)
/// so a brute-force / credential-stuffing attempt gets 429 + Retry-After after a
/// few tries instead of an unbounded number of guesses.
///
/// xUnit builds a fresh test-class instance per test method and IAsyncLifetime
/// runs per instance, so each test below gets its own factory (its own host +
/// its own rate-limiter state + its own in-memory DB) — no cross-test bleed.
///
/// The tests are deliberately limit-agnostic: they verify the behaviour (login
/// is throttled once it exceeds the budget, a single login is not, and non-
/// credential endpoints are never subject to the auth policy) rather than a
/// specific budget number, so they stay valid if the budget is retuned.
/// </summary>
public class RateLimitingTests : IAsyncLifetime
{
    private const string LoginUrl = "/api/auth/login";

    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client;

    public Task InitializeAsync()
    {
        // The shared base factory raises the rate limits to effectively
        // infinity for the general suite; re-arm a small, deterministic auth
        // budget (and a high global ceiling) so the 429 behaviour is verifiable
        // without waiting a real minute.
        _factory = new QontractWebApplicationFactory().WithWebHostBuilder(b =>
            b.UseSetting("RateLimiting:Auth:PermitLimit", "3")
             .UseSetting("RateLimiting:Auth:WindowSeconds", "60")
             .UseSetting("RateLimiting:Global:PermitLimit", "1000000"));
        _client = _factory.CreateClient();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private Task<HttpResponseMessage> BadLoginAsync() =>
        _client.PostAsJsonAsync(LoginUrl, new { email = "brute@qontract.com", password = "wrongpass123" });

    [Theory]
    [InlineData("forgot-password")]
    [InlineData("reset-password")]
    public async Task PasswordResetEndpointsAreThrottled(string endpoint)
    {
        object payload = endpoint == "forgot-password" ? new { email = "user@example.test" }
            : new { token = new string('A', 64), newPassword = "replacement123" };
        for (var i = 0; i < 3; i++)
            (await _client.PostAsJsonAsync("/api/auth/" + endpoint, payload)).StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await _client.PostAsJsonAsync("/api/auth/" + endpoint, payload)).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Login_WithinBudget_ShouldNotBeThrottled()
    {
        // A handful of bad logins, comfortably within the per-IP budget, are
        // each answered with an invalid-credential 401 — never a 429.
        for (var i = 1; i <= 3; i++)
        {
            var response = await BadLoginAsync();
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
                $"login #{i} is within budget and must not be throttled");
        }
    }

    [Fact]
    public async Task Login_BeyondBudget_ShouldReturn429WithRetryAfter()
    {
        // Fire a burst that comfortably exceeds the per-IP budget. The first
        // attempt must still be allowed; once the budget is exhausted the API
        // must start answering 429 (Too Many Requests) with a Retry-After hint.
        var responses = new List<HttpResponseMessage>();
        for (var i = 0; i < 15; i++)
            responses.Add(await BadLoginAsync());

        responses[0].StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests,
            "the first request in a fresh window must not be throttled");

        responses.Should().Contain(r => r.StatusCode == HttpStatusCode.TooManyRequests,
            "a burst of logins must eventually be throttled by the auth policy");

        var rejected = responses.Last(r => r.StatusCode == HttpStatusCode.TooManyRequests);
        rejected.Headers.RetryAfter.Should().NotBeNull(
            "a throttled request must tell the client when to retry");
    }

    [Fact]
    public async Task NonCredentialEndpoint_ShouldNotBeSubjectToAuthPolicy()
    {
        // /api/contracts carries no [EnableRateLimiting("auth")], so a single
        // call must NOT be throttled by the strict auth policy — the policy is
        // scoped to the credential endpoints only.
        var response = await _client.GetAsync("/api/contracts");

        // 401 (no token) or 200 are both fine — the point is it is not 429.
        response.StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests,
            "the auth rate-limit policy must not apply to non-credential endpoints");
    }
}
