using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Qontract.Infrastructure.Data;

namespace Qontract.Infrastructure.PostgreSql;

/// <summary>
/// Used by <c>dotnet ef migrations add ... --project Qontract.Infrastructure.PostgreSql
/// --startup-project Qontract.Infrastructure.PostgreSql</c>. Generating a
/// migration never connects, so the connection string is a placeholder.
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
        optionsBuilder.Configure(DatabaseProvider.PostgreSql, "Host=localhost;Database=qontract_design");

        return new ApplicationDbContext(optionsBuilder.Options);
    }
}
