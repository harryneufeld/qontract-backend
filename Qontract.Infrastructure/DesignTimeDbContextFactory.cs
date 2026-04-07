using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Qontract.Infrastructure.Data;

namespace Qontract.Infrastructure;

public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
        optionsBuilder.UseSqlite("Data Source=QontractDb.sqlite");

        return new ApplicationDbContext(optionsBuilder.Options);
    }
}