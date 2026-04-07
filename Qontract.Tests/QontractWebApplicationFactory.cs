using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Qontract.Infrastructure.Data;

namespace Qontract.Tests;

public class QontractWebApplicationFactory : WebApplicationFactory<Program>, IDisposable
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Remove existing DbContext registration
            var dbContextDescriptor = services.SingleOrDefault(d =>
                d.ServiceType == typeof(DbContextOptions<ApplicationDbContext>));

            if (dbContextDescriptor != null)
            {
                services.Remove(dbContextDescriptor);
            }

            // Add in-memory SQLite for testing
            services.AddDbContext<ApplicationDbContext>(options =>
            {
                options.UseSqlite("Data Source=:memory:");
            });
        });

    }

    public override void Dispose()
    {
        // Clean up if needed
        base.Dispose();
    }

    protected override void ConfigureLogging(logging =>
    {
        logging.ClearProviders();
        logging.AddConsole();
    });
}