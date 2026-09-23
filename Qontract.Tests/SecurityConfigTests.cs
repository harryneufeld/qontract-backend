using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;
using Qontract.API;
using Qontract.Application.Services;
using Qontract.Infrastructure.Data;

namespace Qontract.Tests;

/// <summary>
/// Regression tests for issue #36 (security config hardening):
///
/// 1. The weak, committed <c>Jwt:Key</c> is gone from appsettings.json, and a
///    non-Development environment without a key fails fast at startup with a
///    clear message (instead of silently signing with a placeholder).
/// 2. EF Core sensitive data logging (SQL parameters incl. password hashes on
///    login) is enabled only in Development.
/// 3. The Development fallback key is the single shared constant
///    <c>Qontract.Core.Security.JwtKeys.DevelopmentFallback</c> referenced by
///    BOTH token issuing (AuthService) and validation (Program.cs); an
///    end-to-end login + authenticated request round trip in Development
///    without any Jwt:Key configured proves the two sides still agree.
/// </summary>
public class SecurityConfigTests : IClassFixture<QontractWebApplicationFactory>
{
    private readonly QontractWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public SecurityConfigTests(QontractWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public void CommittedAppsettings_DoNotContainJwtKey()
    {
        // Repo layout: Qontract.Tests/bin/<config>/net10.0/ -> four levels up is the solution root.
        var appsettingsPath = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Qontract.API", "appsettings.json"));
        File.Exists(appsettingsPath).Should().BeTrue($"Qontract.API/appsettings.json not found at {appsettingsPath}");

        using var doc = JsonDocument.Parse(File.ReadAllText(appsettingsPath));
        if (doc.RootElement.TryGetProperty("Jwt", out var jwt))
        {
            jwt.TryGetProperty("Key", out _).Should().BeFalse(
                "a committed Jwt:Key passes the >=32 byte validation and lets anyone with repo " +
                "access mint valid tokens for any user/tenant (issue #36). Provide it via the " +
                "Jwt__Key environment variable or user secrets instead.");
        }
    }

    [Fact]
    public void ProductionStartup_FailsFast_WhenJwtKeyMissing()
    {
        using var factory = new PlainHostFactory("Production", jwtKey: null);
        var ex = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        var messages = string.Join(" | ",
            new[] { ex.Message, ex.InnerException?.Message }.Where(m => !string.IsNullOrEmpty(m)));
        messages.Should().Contain("Jwt:Key is not configured");
    }

    [Fact]
    public async Task ProductionStartup_Succeeds_WhenJwtKeyProvided()
    {
        using var factory = new PlainHostFactory("Production",
            "production-test-key-0123456789-0123456789");

        var client = factory.CreateClient();
        factory.Services.GetRequiredService<IAuthService>().Should().NotBeNull();

        var response = await client.GetAsync("/api/contracts");
        // 401 (no token) is the expected healthy answer; 500 would mean the
        // host/pipeline is broken.
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public void SensitiveDataLogging_EnabledOnlyInDevelopment()
    {
        using var dev = new PlainHostFactory("Development", jwtKey: null);
        using var devScope = dev.Services.CreateScope();
        var devOptions = devScope.ServiceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>();
        devOptions.Extensions
            .OfType<CoreOptionsExtension>()
            .Single()
            .IsSensitiveDataLoggingEnabled.Should().BeTrue(
                "sensitive data logging is expected to remain available in Development");

        using var prod = new PlainHostFactory("Production",
            "production-test-key-0123456789-0123456789");
        using var prodScope = prod.Services.CreateScope();
        var prodOptions = prodScope.ServiceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>();
        prodOptions.Extensions
            .OfType<CoreOptionsExtension>()
            .Single()
            .IsSensitiveDataLoggingEnabled.Should().BeFalse(
                "sensitive data logging leaks SQL parameter values (incl. password hashes) " +
                "into production logs (issue #36)");
    }

    [Fact]
    public async Task DevelopmentFallbackKey_IssuedTokensAreAcceptedByValidationPipeline()
    {
        // The shared test host runs in Development with NO Jwt:Key configured
        // anywhere, so issuing (AuthService) and validation (Program.cs) both
        // use Qontract.Core.Security.JwtKeys.DevelopmentFallback. If the two
        // sides ever drift apart again (duplicated literals, issue #36), this
        // round trip breaks.
        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login",
            new { email = "admin@qontract.com", password = "password123" });
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var auth = await loginResponse.Content.ReadFromJsonAsync<JsonElement>();
        var token = auth.GetProperty("accessToken").GetString();
        token.Should().NotBeNullOrEmpty();

        _client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var response = await _client.GetAsync("/api/contracts");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// Host factory that keeps the app's OWN DbContext registration
    /// (unlike QontractWebApplicationFactory, which swaps it for a shared
    /// in-memory connection) so the tests observe the real Program.cs
    /// options. Uses a throwaway SQLite file in the gitignored test output
    /// directory.
    /// </summary>
    private sealed class PlainHostFactory : WebApplicationFactory<Program>
    {
        private readonly string _environment;
        private readonly string? _jwtKey;
        private readonly string _dbPath;

        public PlainHostFactory(string environment, string? jwtKey)
        {
            _environment = environment;
            _jwtKey = jwtKey;
            _dbPath = Path.Combine(AppContext.BaseDirectory,
                $"security-config-test-{Guid.NewGuid():N}.sqlite");
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(_environment);
            builder.UseSetting("ConnectionStrings:DefaultConnection", $"Data Source={_dbPath}");
            if (_jwtKey is not null)
            {
                builder.UseSetting("Jwt:Key", _jwtKey);
            }
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing && File.Exists(_dbPath))
            {
                try
                {
                    File.Delete(_dbPath);
                }
                catch
                {
                    // best effort cleanup of a throwaway test database
                }
            }
        }
    }
}
