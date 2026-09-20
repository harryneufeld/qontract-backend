using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Qontract.Application.Models;

namespace Qontract.Tests;

/// <summary>
/// Verifies the exception-handling middleware (issue #11): unhandled
/// exceptions are converted into a clean, consistent JSON error envelope
/// and never leak stack traces to clients.
/// </summary>
public class ExceptionHandlingIntegrationTests : IClassFixture<QontractWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly IWebHostEnvironment _environment;

    public ExceptionHandlingIntegrationTests(QontractWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
        _environment = factory.Services.GetRequiredService<IWebHostEnvironment>();
    }

    private async Task<string> GetTokenAsync(string email)
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new { email, password = "password123" });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var auth = await response.Content.ReadFromJsonAsync<AuthDTO>();
        return auth!.AccessToken;
    }

    private async Task<JsonElement> GetErrorEnvelopeAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        // Consistent envelope shape
        root.TryGetProperty("title", out _).Should().BeTrue();
        root.TryGetProperty("status", out _).Should().BeTrue();
        root.TryGetProperty("code", out _).Should().BeTrue();
        root.TryGetProperty("message", out _).Should().BeTrue();

        // The stack trace may ONLY appear inside the `details` field, which is
        // only present in Development. Re-serialize the response without
        // `details` and make sure no stack-trace fragment leaked elsewhere.
        var obj = JsonNode.Parse(body)!.AsObject();
        var withoutDetails = new JsonObject();
        foreach (var kv in obj)
        {
            if (kv.Key != "details")
                withoutDetails[kv.Key] = kv.Value.DeepClone();
        }
        var bodyWithoutDetails = withoutDetails.ToJsonString();
        bodyWithoutDetails.Should().NotContain("at Qontract.");
        bodyWithoutDetails.Should().NotContain("System.Private.CoreLib");
        bodyWithoutDetails.Should().NotContain("stackTrace");

        // details field is only present in Development, and carries the type
        // and stack trace there.
        var isDev = _environment.EnvironmentName == "Development";
        root.TryGetProperty("details", out _).Should().Be(isDev);
        if (isDev)
        {
            var details = root.GetProperty("details");
            details.GetProperty("type").GetString().Should().NotBeNullOrEmpty();
            details.GetProperty("stackTrace").GetString()!
                .Should().Contain("at Qontract.");
        }

        return root;
    }

    [Fact]
    public async Task UnknownContractId_ReturnsClean404_WithoutStackTrace()
    {
        var token = await GetTokenAsync("admin@qontract.com");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.GetAsync("/api/contracts/999999");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var root = await GetErrorEnvelopeAsync(response);
        root.GetProperty("code").GetString().Should().Be("not_found");
        // Message is intentionally generic (no cross-tenant info leak).
        root.GetProperty("message").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task InvalidLogin_ReturnsClean400_Not500()
    {
        // AuthService throws InvalidOperationException for bad credentials.
        // The controller currently catches it (401) — either is acceptable,
        // but the body must be a clean envelope or at least no stack trace.
        var response = await _client.PostAsJsonAsync("/api/auth/login", new
        {
            email = "admin@qontract.com",
            password = "wrong-password"
        });

        response.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain("StackTrace");
        body.Should().NotContain("at Qontract.");
    }

    [Fact]
    public async Task InvalidModel_Returns400_WithErrors_NotStackTrace()
    {
        var token = await GetTokenAsync("admin@qontract.com");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.PostAsJsonAsync("/api/contracts", new
        {
            title = "", // [Required]
            status = "Active"
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain("StackTrace");
        body.Should().NotContain("at Qontract.");
    }

    [Fact]
    public async Task CrossTenantAccess_Returns404_Not500()
    {
        // Tenant 1 creates a contractor
        var token1 = await GetTokenAsync("admin@qontract.com");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token1);

        var created = await _client.PostAsJsonAsync("/api/contractors", new
        {
            name = "Cross Tenant Probe",
            type = "Company",
            contactInfo = "probe@qontract.com"
        });
        created.StatusCode.Should().BeOneOf(HttpStatusCode.Created, HttpStatusCode.OK);
        var createdJson = await created.Content.ReadFromJsonAsync<JsonElement>();
        var id = createdJson.GetProperty("id").GetInt32();

        // Tenant 2 tries to read it
        var token2 = await GetTokenAsync("admin2@qontract.com");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token2);

        var response = await _client.GetAsync($"/api/contractors/{id}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var root = await GetErrorEnvelopeAsync(response);
        root.GetProperty("code").GetString().Should().Be("not_found");
    }
}
