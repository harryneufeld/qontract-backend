using FluentAssertions;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;
using Qontract.Application.Models;

namespace Qontract.Tests;

/// <summary>
/// Issue #33: user DTOs must never expose password material.
/// <see cref="UserDTO"/> previously carried a <c>PasswordHash</c> property
/// that <c>GET /api/users</c> and <c>GET /api/users/{id}</c> serialized,
/// letting every tenant member read the salted-SHA256 hashes of all other
/// users. The property has been removed and the create/update DTOs now
/// accept the plain password in a field that is honestly named
/// <c>password</c>.
/// </summary>
public class UserDtoSecretLeakTests : IClassFixture<QontractWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly QontractWebApplicationFactory _factory;

    // The API serializes enums as (camelCase) strings — mirror that when
    // deserializing responses in tests.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public UserDtoSecretLeakTests(QontractWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task AuthenticateAsAdminAsync()
    {
        var loginResponse = await _client.PostAsJsonAsync(
            "/api/auth/login", new { email = "admin@qontract.com", password = "password123" });
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var auth = await loginResponse.Content.ReadFromJsonAsync<AuthDTO>();
        auth.Should().NotBeNull();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.AccessToken);
    }

    [Fact]
    public async Task GetUserList_NeverContainsPasswordMaterial()
    {
        await AuthenticateAsAdminAsync();

        var response = await _client.GetAsync("/api/users");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();

        // The raw JSON must not mention password hashes (or any password) —
        // not the old "passwordHash" field, not a plain "password" field.
        json.ToLowerInvariant().Should().NotContain("password");
    }

    [Fact]
    public async Task GetUserById_NeverContainsPasswordMaterial()
    {
        await AuthenticateAsAdminAsync();

        var response = await _client.GetAsync("/api/users/1");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();

        json.ToLowerInvariant().Should().NotContain("password");
    }

    [Fact]
    public async Task UpdateUser_ReturnsUserWithoutPasswordMaterial()
    {
        await AuthenticateAsAdminAsync();

        var update = await _client.PutAsJsonAsync("/api/users/1", new { role = "TenantAdmin" });
        update.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await update.Content.ReadAsStringAsync();
        json.ToLowerInvariant().Should().NotContain("password");
    }
}
