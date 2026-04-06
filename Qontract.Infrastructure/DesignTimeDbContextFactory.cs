using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Qontract.Infrastructure.Data;
using Qontract.Infrastructure.Data.Migrations;

public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<MigrationsContext>
{
    public MigrationsContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<MigrationsContext>();
        optionsBuilder.UseSqlite("Data Source=QontractDb.sqlite");

        return new MigrationsContext(optionsBuilder.Options);
    }
}