using FluentAssertions;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using Xunit;
using Qontract.API;
using Qontract.Application.Models;

namespace Qontract.Tests;

public class ApiIntegrationTests : IClassFixture<QontractWebApplicationFactory>
{
    private readonly HttpClient _client;

    public ApiIntegrationTests(QontractWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Login_ShouldReturnToken_WithValidCredentials()
    {
        // Arrange
        var loginData = new
        {
            email = "admin@qontract.com",
            password = "password123"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/auth/login", loginData);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadFromJsonAsync<AuthResponse>();
        content.Should().NotBeNull();
        content!.AccessToken.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Login_ShouldReturnUnauthorized_WithInvalidPassword()
    {
        // Arrange
        var loginData = new
        {
            email = "admin@qontract.com",
            password = "wrongpassword"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/auth/login", loginData);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // Mirrors AuthDTO (camelCase JSON: accessToken, email, userId, expiresAt)
    private record AuthResponse(string AccessToken, string? Email = null, int? UserId = null, DateTime? ExpiresAt = null);
}
