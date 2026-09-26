using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Qontract.Domain.Models;
using Qontract.Infrastructure.Data;
using Xunit;

namespace Qontract.Tests;

/// <summary>
/// Tests for the provider abstraction (issue #26): provider selection,
/// cross-provider model/DDL rendering and the portable client-side key
/// generator.
/// </summary>
public class DatabaseProviderTests
{
    [Theory]
    [InlineData(null, "sqlite")]
    [InlineData("", "sqlite")]
    [InlineData("sqlite", "sqlite")]
    [InlineData("SQLite", "sqlite")]
    [InlineData("  sqlite  ", "sqlite")]
    [InlineData("postgresql", "postgresql")]
    [InlineData("postgres", "postgresql")]
    [InlineData("pg", "postgresql")]
    [InlineData("npgsql", "postgresql")]
    [InlineData("sqlserver", "sqlserver")]
    [InlineData("mssql", "sqlserver")]
    [InlineData("sql-server", "sqlserver")]
    public void Normalize_maps_all_aliases(string? raw, string expected)
        => Assert.Equal(expected, DatabaseProvider.Normalize(raw));

    [Theory]
    [InlineData("oracle")]
    [InlineData("mysql")]
    [InlineData("SQLite2")]
    public void Normalize_throws_on_unknown_values(string raw)
        => Assert.Throws<InvalidOperationException>(() => DatabaseProvider.Normalize(raw));

    [Fact]
    public void Configure_wires_the_requested_provider_extension()
    {
        var sqlite = new DbContextOptionsBuilder<ApplicationDbContext>();
        sqlite.Configure("sqlite", "Data Source=x");
        Assert.Contains(sqlite.Options.Extensions,
            e => e.GetType().Name == "SqliteOptionsExtension");

        var pg = new DbContextOptionsBuilder<ApplicationDbContext>();
        pg.Configure("postgres", "Host=localhost");
        Assert.Contains(pg.Options.Extensions,
            e => e.GetType().Name == "NpgsqlOptionsExtension");

        var mssql = new DbContextOptionsBuilder<ApplicationDbContext>();
        mssql.Configure("mssql", "Server=localhost");
        Assert.Contains(mssql.Options.Extensions,
            e => e.GetType().Name == "SqlServerOptionsExtension");
    }

    [Theory]
    [InlineData("sqlite", "Data Source=x")]
    [InlineData("postgresql", "Host=localhost")]
    [InlineData("sqlserver", "Server=localhost")]
    public void Model_builds_and_schema_script_renders_for_every_provider(string provider, string connection)
    {
        // The script is generated from the shared migration chain, so this
        // verifies that the migration DDL renders for the target provider
        // (no live database needed).
        var builder = new DbContextOptionsBuilder<ApplicationDbContext>();
        builder.Configure(provider, connection);
        using var context = new ApplicationDbContext(builder.Options);

        var model = context.Model; // builds + validates against the provider
        Assert.NotNull(model.FindEntityType(typeof(Tenant)));

        var script = context.Database.GenerateCreateScript();
        Assert.Contains("Tenants", script);
        Assert.Contains("Contracts", script);
        Assert.Contains("Documents", script);
    }

    [Fact]
    public void ClientSideIdGenerator_seeds_from_existing_rows_and_continues_across_contexts()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        try
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlite(connection).Options;

            using (var context = new ApplicationDbContext(options))
            {
                context.Database.EnsureCreated();

                // Simulate an existing database with rows 1..7
                for (var i = 1; i <= 7; i++)
                {
                    context.Database.ExecuteSqlRaw(
                        "INSERT INTO Tenants (Id, Name, Type, IsActive, CreatedAt, ModifiedAt) " +
                        "VALUES (@id, @name, 0, 1, '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z')",
                        new Microsoft.Data.Sqlite.SqliteParameter("@id", i),
                        new Microsoft.Data.Sqlite.SqliteParameter("@name", "existing" + i));
                }

                for (var i = 0; i < 3; i++)
                {
                    context.Tenants.Add(new Tenant
                    {
                        Name = "new" + i,
                        Type = TenantType.Family,
                        IsActive = true
                    });
                    context.SaveChanges();
                }

                // The generated ids continue exactly after the pre-existing
                // rows 1..7 (an in-memory database's counter is per
                // connection, so this is deterministic).
                var ids = context.Tenants.OrderBy(t => t.Id).Select(t => t.Id).ToList();
                Assert.Equal(new List<int> { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }, ids);
            }

            // A fresh context (simulated process restart) re-seeds from the
            // current max and must not collide with the previous rows.
            using (var context = new ApplicationDbContext(options))
            {
                context.Tenants.Add(new Tenant
                {
                    Name = "after restart",
                    Type = TenantType.Family,
                    IsActive = true
                });
                context.SaveChanges();

                Assert.Equal(11, context.Tenants.Single(t => t.Name == "after restart").Id);
            }
        }
        finally
        {
            connection.Close();
        }
    }
}
