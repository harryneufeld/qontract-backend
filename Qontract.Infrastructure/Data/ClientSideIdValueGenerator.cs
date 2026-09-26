using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.ValueGeneration;

namespace Qontract.Infrastructure.Data;

/// <summary>
/// Provider-neutral client-side primary key generator (issue #26).
///
/// EF's default key strategies are provider-specific (SQLite
/// AUTOINCREMENT, PostgreSQL identity, SQL Server IDENTITY), which made the
/// migration chain unusable on PostgreSQL/SQL Server. This generator instead
/// assigns keys on the client: for each table it lazily seeds from
/// <c>SELECT MAX(Id)</c> on first use (so existing databases keep working)
/// and then increments in memory.
///
/// The in-memory counter is keyed per *logical database*:
///  * in-memory SQLite (":memory:") — per physical connection, because each
///    open in-memory connection is a distinct database;
///  * file databases (SQLite file, PostgreSQL, SQL Server) — per connection
///    string, so all pooled connections of one process share one counter
///    and can never hand out the same key twice.
///
/// Remaining trade-off: two *processes* writing to the same database could
/// theoretically assign the same key (the same trade-off SQLite makes with
/// its default value generation). Qontract runs a single backend instance
/// per deployment, so this is not a concern in practice.
/// </summary>
public sealed class ClientSideIdValueGenerator : ValueGenerator
{
    /// <summary>Per-connection counters for in-memory SQLite databases
    /// (one database per open connection), keyed per table.</summary>
    private sealed class CounterBox
    {
        public ConcurrentDictionary<string, int> Counters = new();
    }

    private static readonly ConcurrentDictionary<string, int> _counters = new();
    private static readonly ConditionalWeakTable<object, CounterBox> _memoryCounters = new();
    private static readonly object _lock = new();

    public override bool GeneratesTemporaryValues => false;

    protected override object NextValue(EntityEntry entry)
    {
        var context = entry.Context;
        var entityType = entry.Metadata;
        var tableName = entityType.GetTableName();
        var keyProperty = entityType.FindPrimaryKey()!.Properties.Single();
        var tableKey = tableName + "." + keyProperty.GetColumnName();
        var connection = context.Database.GetDbConnection();

        lock (_lock)
        {
            int next;
            if (IsInMemorySqlite(connection))
            {
                // Each open in-memory connection is its own database, so the
                // counters live on the connection (a fresh connection starts
                // from its own table max — the same behaviour AUTOINCREMENT
                // gave fresh databases).
                var box = _memoryCounters.GetOrCreateValue(connection);
                if (!box.Counters.TryGetValue(tableKey, out var currentMemory))
                {
                    currentMemory = SeedMax(context, entityType, keyProperty);
                    box.Counters[tableKey] = currentMemory;
                }
                next = currentMemory + 1;
                box.Counters[tableKey] = next;
            }
            else
            {
                // All pooled connections of this process share one counter
                // per logical database (provider + connection string).
                var counterKey = DatabaseIdentity(context) + "." + tableKey;
                if (!_counters.TryGetValue(counterKey, out var current))
                {
                    current = SeedMax(context, entityType, keyProperty);
                    _counters[counterKey] = current;
                }
                next = current + 1;
                _counters[counterKey] = next;
            }
            return next;
        }
    }

    private static bool IsInMemorySqlite(object connection)
    {
        // DataSource is empty once the connection is open, so check both it
        // and the connection string.
        if (connection is not Microsoft.Data.Sqlite.SqliteConnection sqlite)
        {
            return false;
        }

        var dataSource = sqlite.DataSource ?? "";
        var connectionString = sqlite.ConnectionString ?? "";
        return dataSource.Contains(":memory:", StringComparison.OrdinalIgnoreCase)
            || connectionString.Contains(":memory:", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Logical database identity (provider + connection string), used as the
    /// counter key for non-in-memory databases.
    /// </summary>
    private static string DatabaseIdentity(DbContext context)
    {
        var provider = context.GetService<IDbContextOptions>().Extensions
            .OfType<RelationalOptionsExtension>()
            .Select(e => e.GetType().Name)
            .FirstOrDefault() ?? "unknown";
        return provider + "|" + (context.Database.GetConnectionString() ?? "");
    }

    /// <summary>
    /// Reads the current maximum key of the table via a normal EF query
    /// (no hand-written SQL, so it works identically on every provider).
    /// Empty table → NULL → treated as 0.
    /// </summary>
    private static int SeedMax(DbContext context, IEntityType entityType, IProperty keyProperty)
    {
        var clrType = entityType.ClrType;

        // Build e => (int?)e.<Key> without generics (entity types are
        // discovered at runtime). The selector is nullable because SQL
        // MAX() over an empty table is NULL.
        var param = Expression.Parameter(clrType, "e");
        var prop = Expression.Property(param, keyProperty.Name);
        var body = Expression.Convert(prop, typeof(int?));
        var lambdaType = typeof(Func<,>).MakeGenericType(clrType, typeof(int?));
        var lambda = Expression.Lambda(lambdaType, body, new[] { param });

        var setMethod = typeof(DbContext).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .First(m => m.Name == "Set" && m.IsGenericMethodDefinition && m.GetParameters().Length == 0);
        var set = (IQueryable)setMethod.MakeGenericMethod(clrType).Invoke(context, null)!;

        // Queryable.Max<TSource, TResult>(IQueryable<TSource>, Expression<Func<TSource,TResult>>)
        var maxMethod = typeof(Queryable).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .First(m =>
            {
                if (m.Name != "Max" || !m.IsGenericMethodDefinition || m.GetParameters().Length != 2)
                    return false;
                var exprParam = m.GetParameters()[1].ParameterType;
                if (!exprParam.IsGenericType || exprParam.GetGenericTypeDefinition() != typeof(Expression<>))
                    return false;
                var func = exprParam.GetGenericArguments()[0];
                return func.IsGenericType
                    && func.GetGenericTypeDefinition() == typeof(Func<,>)
                    && m.GetGenericArguments().Length == 2;
            });
        var generic = maxMethod.MakeGenericMethod(clrType, typeof(int?));
        return (int?)generic.Invoke(null, new object[] { set, lambda }) ?? 0;
    }
}
