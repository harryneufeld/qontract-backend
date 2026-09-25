using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Qontract.Infrastructure.Data;

namespace Qontract.Tests;

public class QontractWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection;

    public QontractWebApplicationFactory()
    {
        // Shared in-memory SQLite: the connection stays open for the lifetime
        // of the factory so every DbContext scope sees the same database.
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddConsole();
            logging.SetMinimumLevel(LogLevel.Information);
        });
        return base.CreateHost(builder);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Pin the environment so environment-dependent behaviour (e.g. the
        // Development-only `details` field of the error envelope, issue #11)
        // is deterministic in tests.
        builder.UseEnvironment("Development");

        // Rate limiting (issue #27) is a per-client-IP security control. In the
        // test suite every request originates from the same loopback IP and each
        // test class shares one host, so a production limit (e.g. 5 logins/min
        // per IP) would throttle the many logins the existing integration tests
        // make. Raise both limits effectively to infinity here so rate limiting
        // is off for the general suite; RateLimitingTests re-arms the auth limit
        // explicitly via WithWebHostBuilder to verify the 429 behaviour.
        builder.UseSetting("RateLimiting:Auth:PermitLimit", "1000000");
        builder.UseSetting("RateLimiting:Global:PermitLimit", "1000000");

        builder.ConfigureServices(services =>
        {
            // Remove existing DbContext registration
            var dbContextDescriptor = services.SingleOrDefault(d =>
                d.ServiceType == typeof(DbContextOptions<ApplicationDbContext>));

            if (dbContextDescriptor != null)
            {
                services.Remove(dbContextDescriptor);
            }

            // Registered as singleton so the connection is closed when the
            // host (and thus this factory) is disposed.
            services.AddSingleton(_connection);

            // Add in-memory SQLite for testing
            services.AddDbContext<ApplicationDbContext>(options =>
            {
                options.UseSqlite(_connection);
            });

            // Remove the document retention background worker (issue #14):
            // it would share the singleton in-memory SQLite connection with
            // the request pipeline. Tests exercise the purge logic directly
            // through DocumentPurgeService instead.
            var workerDescriptor = services.SingleOrDefault(d =>
                d.ServiceType == typeof(IHostedService)
                && d.ImplementationType == typeof(Qontract.API.Services.DocumentRetentionWorker));
            if (workerDescriptor != null)
            {
                services.Remove(workerDescriptor);
            }
        });
    }
}
