using Microsoft.EntityFrameworkCore;

namespace Qontract.Infrastructure.Data;

/// <summary>
/// Database provider selection (issue #26). The backend runs on SQLite by
/// default (development, zero-setup) but can target PostgreSQL or SQL Server
/// for production deployments via the <c>Database:Provider</c> configuration
/// value (environment variable <c>Database__Provider</c>).
///
/// SQLite and SQL Server share the migration chain in this assembly (TEXT /
/// INTEGER columns). PostgreSQL has its own chain with native column types
/// in <see cref="PostgreSqlMigrationsAssembly"/>; an existing SQLite
/// database moves over with <see cref="DatabaseCopier"/>. Primary keys are
/// generated client-side by <see cref="ClientSideIdValueGenerator"/> on
/// every provider.
/// </summary>
public static class DatabaseProvider
{
    public const string Sqlite = "sqlite";
    public const string PostgreSql = "postgresql";
    public const string SqlServer = "sqlserver";

    public const string SharedMigrationsAssembly = "Qontract.Infrastructure";
    public const string PostgreSqlMigrationsAssembly = "Qontract.Infrastructure.PostgreSql";

    /// <summary>
    /// Normalizes the <c>Database:Provider</c> configuration value.
    /// Null/empty means SQLite (the default). Throws on unknown values so a
    /// typo fails fast at startup instead of at first database access.
    /// </summary>
    public static string Normalize(string? raw) =>
        raw?.Trim().ToLowerInvariant() switch
        {
            null or "" or "sqlite" => Sqlite,
            "postgresql" or "postgres" or "pg" or "npgsql" => PostgreSql,
            "sqlserver" or "mssql" or "sql-server" or "sql server" => SqlServer,
            _ => throw new InvalidOperationException(
                $"Unknown Database:Provider '{raw}'. Expected 'sqlite', 'postgresql' or 'sqlserver'.")
        };

    /// <summary>
    /// Configures the <see cref="ApplicationDbContext"/> options for the
    /// given provider, wiring its migration assembly.
    /// </summary>
    public static void Configure(
        this DbContextOptionsBuilder options,
        string provider,
        string connectionString)
    {
        switch (Normalize(provider))
        {
            case Sqlite:
                options.UseSqlite(connectionString,
                    b => b.MigrationsAssembly(SharedMigrationsAssembly));
                break;
            case PostgreSql:
                options.UseNpgsql(connectionString,
                    b => b.MigrationsAssembly(PostgreSqlMigrationsAssembly));
                break;
            case SqlServer:
                options.UseSqlServer(connectionString,
                    b => b.MigrationsAssembly(SharedMigrationsAssembly));
                break;
        }
    }
}
