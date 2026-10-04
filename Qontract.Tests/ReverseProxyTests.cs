using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Qontract.Tests;

public class ReverseProxyTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ForwardedClientIp_ChangesAuthLimiterOnlyForTrustedProxy(bool trusted)
    {
        using var root = new QontractWebApplicationFactory();
        var observation = new Observation();
        using var factory = root.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("RateLimiting:Auth:PermitLimit", "1");
            builder.UseSetting("RateLimiting:Auth:WindowSeconds", "600");
            builder.UseSetting("ReverseProxy:KnownProxies:0", trusted ? "192.0.2.10" : "192.0.2.99");
            builder.ConfigureServices(services =>
                services.AddSingleton<IStartupFilter>(new PeerFilter(observation)));
        });
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });

        async Task<HttpStatusCode> Login(string address)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
            {
                Content = JsonContent.Create(new { email = "missing@example.test", password = "password123" })
            };
            request.Headers.Add("X-Forwarded-For", address);
            request.Headers.Add("X-Forwarded-Proto", "https");
            using var response = await client.SendAsync(request);
            return response.StatusCode;
        }

        Assert.Equal(HttpStatusCode.Unauthorized, await Login("198.51.100.1"));
        Assert.Equal(trusted ? "https" : "http", observation.Scheme);
        Assert.Equal(trusted ? "198.51.100.1" : "192.0.2.10", observation.Address);
        Assert.Equal(trusted ? HttpStatusCode.Unauthorized : HttpStatusCode.TooManyRequests,
            await Login("198.51.100.2"));
        Assert.Equal(HttpStatusCode.TooManyRequests, await Login("198.51.100.1"));
    }

    [Fact]
    public void InvalidProxyAddress_FailsStartup()
    {
        using var root = new QontractWebApplicationFactory();
        using var factory = root.WithWebHostBuilder(builder =>
            builder.UseSetting("ReverseProxy:KnownProxies:0", "trust-everyone"));
        Assert.ThrowsAny<Exception>(() => factory.CreateClient());
    }

    private sealed class Observation
    {
        public string? Address { get; set; }
        public string? Scheme { get; set; }
    }

    private sealed class PeerFilter(Observation observation) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, nextMiddleware) =>
            {
                context.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.10");
                await nextMiddleware();
                observation.Address = context.Connection.RemoteIpAddress?.ToString();
                observation.Scheme = context.Request.Scheme;
            });
            next(app);
        };
    }
}
