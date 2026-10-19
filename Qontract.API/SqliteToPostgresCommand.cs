using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Qontract.Infrastructure.Data;

namespace Qontract.API;

/// <summary>
/// <c>dotnet Qontract.API.dll migrate-sqlite-to-postgres --source &lt;file or connection string&gt;</c>
///
/// Copies an existing SQLite database into the configured PostgreSQL
/// database (Database:Provider=postgresql, ConnectionStrings:DefaultConnection)
/// and exits without starting the web host. The SQLite file is opened
/// read-only; see DEPLOY.md for the cut-over steps.
/// </summary>
internal static class SqliteToPostgresCommand
{
    public const string Name = "migrate-sqlite-to-postgres";

    public static async Task<int> RunAsync(string[] args, string targetProvider, string targetConnectionString)
    {
        var source = args.Length == 2 && args[0] == "--source" ? args[1] : null;
        if (string.IsNullOrWhiteSpace(source))
        {
            Console.Error.WriteLine($"Usage: {Name} --source <path to .sqlite file | SQLite connection string>");
            return 2;
        }
        if (targetProvider != DatabaseProvider.PostgreSql)
        {
            Console.Error.WriteLine("Set Database__Provider=postgresql and ConnectionStrings__DefaultConnection to the target PostgreSQL database.");
            return 2;
        }

        var sourceConnection = new SqliteConnectionStringBuilder(source.Contains('=') ? source : $"Data Source={source}")
        {
            Mode = SqliteOpenMode.ReadOnly
        };
        if (!File.Exists(sourceConnection.DataSource))
        {
            Console.Error.WriteLine($"SQLite file not found: {sourceConnection.DataSource}");
            return 2;
        }

        await using var sourceContext = Create(DatabaseProvider.Sqlite, sourceConnection.ToString());
        await using var targetContext = Create(DatabaseProvider.PostgreSql, targetConnectionString);
        try
        {
            var report = await DatabaseCopier.CopyAsync(sourceContext, targetContext, Console.WriteLine);
            Console.WriteLine($"Done: {report.Tables.Sum(t => t.Rows)} row(s) in {report.Tables.Count} table(s) copied to PostgreSQL.");
            return 0;
        }
        catch (DatabaseCopyException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
        catch (Exception ex) when (ex is System.Data.Common.DbException or InvalidOperationException)
        {
            Console.Error.WriteLine($"Copy failed and was rolled back: {ex.GetBaseException().Message}");
            return 1;
        }
    }

    private static ApplicationDbContext Create(string provider, string connectionString)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>();
        options.Configure(provider, connectionString);
        return new ApplicationDbContext(options.Options);
    }
}
