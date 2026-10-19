using System.Collections.Concurrent;
using System.Data.Common;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;

namespace Qontract.Infrastructure.Data;

public sealed record TableCopyResult(string Table, long Rows);

public sealed record DatabaseCopyReport(
    IReadOnlyList<TableCopyResult> Tables,
    IReadOnlyList<string> SkippedSourceTables);

public sealed class DatabaseCopyException(string message) : Exception(message);

/// <summary>
/// Copies every table of the Qontract model from one database into an empty
/// database of another provider (SQLite → PostgreSQL). Values go through EF's
/// own type mappings on both sides, so SQLite's TEXT/INTEGER storage turns
/// into native PostgreSQL types (timestamptz, uuid, boolean, date) without
/// hand-written conversions.
///
/// Safety rails: the source must be at exactly the schema this release
/// expects, the target must be empty, every value is checked against the
/// target's constraints before anything is written, the copy runs in one
/// target transaction and is only committed after row counts and per-table
/// checksums match. The source is only read.
/// </summary>
public static class DatabaseCopier
{
    private const int RowsPerInsert = 200;
    private const string MigrationsLockTable = "__EFMigrationsLock";

    public static async Task<DatabaseCopyReport> CopyAsync(
        ApplicationDbContext source,
        ApplicationDbContext target,
        Action<string>? log = null,
        CancellationToken cancellationToken = default)
    {
        log ??= _ => { };
        await EnsureSourceSchemaIsCurrentAsync(source, cancellationToken);

        log("Applying target migrations...");
        await target.Database.MigrateAsync(cancellationToken);

        var plan = TablePlan.Create(source, target);
        var skipped = await FindUnmappedSourceTablesAsync(source, plan, cancellationToken);
        foreach (var table in skipped)
            log($"Skipping source table without a model mapping: {table}");

        await EnsureTargetIsEmptyAsync(target, plan, cancellationToken);
        await ValidateSourceAsync(source, plan, cancellationToken);

        var targetConnection = target.Database.GetDbConnection();
        await target.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var transaction = await target.Database.BeginTransactionAsync(cancellationToken);
            var dbTransaction = transaction.GetDbTransaction();
            var results = new List<TableCopyResult>();
            var deferred = new List<DeferredUpdate>();
            var sourceChecksums = new Dictionary<string, string>();

            foreach (var table in plan.Tables)
            {
                var (rows, checksum) = await CopyTableAsync(source, target, table, dbTransaction, deferred, cancellationToken);
                sourceChecksums[table.Name] = checksum;
                results.Add(new TableCopyResult(table.Name, rows));
                log($"Copied {rows} row(s) into {table.Name}");
            }

            foreach (var update in deferred)
                await ApplyDeferredUpdateAsync(target, update, dbTransaction, cancellationToken);
            if (deferred.Count > 0)
                log($"Restored {deferred.Count} self/cyclic reference(s)");

            foreach (var table in plan.Tables)
            {
                var (rows, checksum) = await ReadChecksumAsync(target, table, table.TargetColumns, table.TargetSql, dbTransaction, cancellationToken);
                var expected = results.Single(r => r.Table == table.Name).Rows;
                if (rows != expected || checksum != sourceChecksums[table.Name])
                    throw new DatabaseCopyException(
                        $"Verification failed for {table.Name}: copied {rows}/{expected} row(s), checksums differ: {checksum != sourceChecksums[table.Name]}. Nothing was committed.");
            }

            await transaction.CommitAsync(cancellationToken);
            log("Verified row counts and checksums; committed.");
            return new DatabaseCopyReport(results, skipped);
        }
        finally
        {
            await target.Database.CloseConnectionAsync();
        }
    }

    private static async Task EnsureSourceSchemaIsCurrentAsync(ApplicationDbContext source, CancellationToken cancellationToken)
    {
        if (!await source.Database.CanConnectAsync(cancellationToken))
            throw new DatabaseCopyException("Cannot open the source database.");
        var pending = (await source.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
        if (pending.Count > 0)
            throw new DatabaseCopyException(
                $"The source database is missing {pending.Count} migration(s) (first: {pending[0]}). Start this release once on the source database so it upgrades itself, then copy again.");
        var known = source.Database.GetMigrations().ToHashSet();
        var unknown = (await source.Database.GetAppliedMigrationsAsync(cancellationToken)).Where(m => !known.Contains(m)).ToList();
        if (unknown.Count > 0)
            throw new DatabaseCopyException(
                $"The source database was migrated by a newer release ({unknown[^1]}). Copy it with that release instead.");
    }

    private static async Task EnsureTargetIsEmptyAsync(ApplicationDbContext target, TablePlan plan, CancellationToken cancellationToken)
    {
        var occupied = new List<string>();
        foreach (var table in plan.Tables)
        {
            await using var command = await CreateCommandAsync(target, $"SELECT COUNT(*) FROM {table.TargetSql}", null, cancellationToken);
            if (Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture) > 0)
                occupied.Add(table.Name);
        }
        if (occupied.Count > 0)
            throw new DatabaseCopyException(
                $"The target database already contains data ({string.Join(", ", occupied)}). Copy only into a freshly created, empty database.");
    }

    private static async Task<IReadOnlyList<string>> FindUnmappedSourceTablesAsync(
        ApplicationDbContext source, TablePlan plan, CancellationToken cancellationToken)
    {
        if (!source.Database.IsSqlite()) return [];
        var names = new List<string>();
        await using var command = await CreateCommandAsync(source,
            "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite\\_%' ESCAPE '\\' ORDER BY name", null, cancellationToken);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            names.Add(reader.GetString(0));
        // EF's own bookkeeping (history and the EF 9+ migration lock) is not data.
        var mapped = plan.Tables.Select(t => t.Name).Append(HistoryTableName(source)).Append(MigrationsLockTable)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return names.Where(n => !mapped.Contains(n)).ToList();
    }

    private static string HistoryTableName(DbContext context) =>
        context.GetService<IDbContextOptions>().Extensions.OfType<RelationalOptionsExtension>()
            .FirstOrDefault()?.MigrationsHistoryTableName ?? HistoryRepository.DefaultTableName;

    /// <summary>
    /// Reads the whole source once and reports every value the target would
    /// reject (NULL in a required column, text longer than the column, NUL
    /// characters PostgreSQL cannot store), so a copy fails up front with a
    /// complete list instead of on the first bad row.
    /// </summary>
    private static async Task ValidateSourceAsync(ApplicationDbContext source, TablePlan plan, CancellationToken cancellationToken)
    {
        var problems = new List<string>();
        foreach (var table in plan.Tables)
        {
            await using var command = await CreateCommandAsync(source, table.SelectSql(source, table.SourceColumns, table.SourceSql), null, cancellationToken);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var values = ReadRow(reader, table.SourceColumns);
                for (var i = 0; i < values.Length; i++)
                {
                    var column = table.TargetColumns[i];
                    var where = $"{table.Name}.{column.Name} (key {FormatKey(table, values)})";
                    if (values[i] is null && !column.IsNullable)
                        problems.Add($"{where}: NULL in a required column");
                    if (values[i] is string text)
                    {
                        if (column.MaxLength is { } max && text.Length > max)
                            problems.Add($"{where}: {text.Length} characters, column allows {max}");
                        if (text.Contains('\0'))
                            problems.Add($"{where}: contains a NUL character");
                    }
                }
            }
        }
        if (problems.Count > 0)
            throw new DatabaseCopyException(
                "The source data does not fit the target schema; fix these values first:" + Environment.NewLine
                + string.Join(Environment.NewLine, problems.Take(200).Select(p => "  - " + p))
                + (problems.Count > 200 ? $"{Environment.NewLine}  ... and {problems.Count - 200} more" : ""));
    }

    private static async Task<(long Rows, string Checksum)> CopyTableAsync(
        ApplicationDbContext source, ApplicationDbContext target, PlannedTable table, DbTransaction transaction,
        List<DeferredUpdate> deferred, CancellationToken cancellationToken)
    {
        await using var command = await CreateCommandAsync(source, table.SelectSql(source, table.SourceColumns, table.SourceSql), null, cancellationToken);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var batch = new List<object?[]>(RowsPerInsert);
        long rows = 0;
        while (await reader.ReadAsync(cancellationToken))
        {
            var values = ReadRow(reader, table.SourceColumns);
            AppendCanonical(hash, values);
            foreach (var index in table.DeferredColumns)
            {
                if (values[index] is null) continue;
                deferred.Add(new DeferredUpdate(table, index, values[index], table.KeyIndexes.Select(k => values[k]).ToArray()));
                values = (object?[])values.Clone();
                values[index] = null;
            }
            batch.Add(values);
            rows++;
            if (batch.Count == RowsPerInsert)
            {
                await InsertAsync(target, table, batch, transaction, cancellationToken);
                batch.Clear();
            }
        }
        if (batch.Count > 0)
            await InsertAsync(target, table, batch, transaction, cancellationToken);
        return (rows, Convert.ToHexString(hash.GetHashAndReset()));
    }

    private static async Task InsertAsync(
        ApplicationDbContext target, PlannedTable table, List<object?[]> rows, DbTransaction transaction, CancellationToken cancellationToken)
    {
        var helper = target.GetService<ISqlGenerationHelper>();
        await using var command = await CreateCommandAsync(target, "", transaction, cancellationToken);
        var sql = new StringBuilder("INSERT INTO ").Append(table.TargetSql).Append(" (")
            .Append(string.Join(", ", table.TargetColumns.Select(c => helper.DelimitIdentifier(c.Name)))).Append(") VALUES ");
        var parameter = 0;
        for (var row = 0; row < rows.Count; row++)
        {
            sql.Append(row == 0 ? "(" : ", (");
            for (var i = 0; i < table.TargetColumns.Count; i++)
            {
                var name = "p" + parameter++;
                command.Parameters.Add(table.TargetMappings[i].CreateParameter(command, name, rows[row][i], nullable: true));
                sql.Append(i == 0 ? "" : ", ").Append(helper.GenerateParameterNamePlaceholder(name));
            }
            sql.Append(')');
        }
        command.CommandText = sql.ToString();
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ApplyDeferredUpdateAsync(
        ApplicationDbContext target, DeferredUpdate update, DbTransaction transaction, CancellationToken cancellationToken)
    {
        var helper = target.GetService<ISqlGenerationHelper>();
        var table = update.Table;
        await using var command = await CreateCommandAsync(target, "", transaction, cancellationToken);
        command.Parameters.Add(table.TargetMappings[update.ColumnIndex].CreateParameter(command, "v", update.Value, nullable: true));
        var conditions = new List<string>();
        for (var k = 0; k < table.KeyIndexes.Count; k++)
        {
            var index = table.KeyIndexes[k];
            command.Parameters.Add(table.TargetMappings[index].CreateParameter(command, "k" + k, update.Key[k], nullable: false));
            conditions.Add($"{helper.DelimitIdentifier(table.TargetColumns[index].Name)} = {helper.GenerateParameterNamePlaceholder("k" + k)}");
        }
        command.CommandText = $"UPDATE {table.TargetSql} SET {helper.DelimitIdentifier(table.TargetColumns[update.ColumnIndex].Name)} = {helper.GenerateParameterNamePlaceholder("v")} WHERE {string.Join(" AND ", conditions)}";
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new DatabaseCopyException($"Could not restore {table.Name}.{table.TargetColumns[update.ColumnIndex].Name}.");
    }

    private static async Task<(long Rows, string Checksum)> ReadChecksumAsync(
        ApplicationDbContext context, PlannedTable table, IReadOnlyList<IColumn> columns, string fromSql, DbTransaction? transaction, CancellationToken cancellationToken)
    {
        await using var command = await CreateCommandAsync(context, table.SelectSql(context, columns, fromSql), transaction, cancellationToken);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long rows = 0;
        while (await reader.ReadAsync(cancellationToken))
        {
            AppendCanonical(hash, ReadRow(reader, columns));
            rows++;
        }
        return (rows, Convert.ToHexString(hash.GetHashAndReset()));
    }

    private static async Task<DbCommand> CreateCommandAsync(
        DbContext context, string sql, DbTransaction? transaction, CancellationToken cancellationToken)
    {
        await context.Database.OpenConnectionAsync(cancellationToken);
        var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction ?? context.Database.CurrentTransaction?.GetDbTransaction();
        return command;
    }

    /// <summary>Reads one row as model (CLR) values through each column's EF type mapping.</summary>
    private static object?[] ReadRow(DbDataReader reader, IReadOnlyList<IColumn> columns)
    {
        var values = new object?[columns.Count];
        for (var i = 0; i < columns.Count; i++)
        {
            if (reader.IsDBNull(i)) continue;
            var mapping = Mapping(columns[i]);
            var providerType = mapping.Converter?.ProviderClrType ?? mapping.ClrType;
            var value = FieldReader(Nullable.GetUnderlyingType(providerType) ?? providerType)(reader, i);
            values[i] = mapping.Converter is { } converter ? converter.ConvertFromProvider(value) : value;
        }
        return values;
    }

    private static RelationalTypeMapping Mapping(IColumn column) => column.PropertyMappings.First().TypeMapping;

    private static readonly ConcurrentDictionary<Type, Func<DbDataReader, int, object?>> FieldReaders = new();
    private static readonly MethodInfo GetFieldValueMethod = typeof(DbDataReader).GetMethod(nameof(DbDataReader.GetFieldValue))!;

    private static Func<DbDataReader, int, object?> FieldReader(Type type) => FieldReaders.GetOrAdd(type, t =>
    {
        var method = GetFieldValueMethod.MakeGenericMethod(t);
        return (reader, ordinal) => method.Invoke(reader, [ordinal]);
    });

    /// <summary>
    /// Provider-neutral rendering used for the source/target checksums.
    /// Timestamps are compared at microsecond precision (PostgreSQL's
    /// resolution; SQLite keeps 100 ns ticks) and without their Kind.
    /// </summary>
    private static void AppendCanonical(IncrementalHash hash, object?[] values)
    {
        var builder = new StringBuilder();
        foreach (var value in values)
        {
            builder.Append(value switch
            {
                null => "␀",
                DateTime d => (d.Ticks - d.Ticks % 10).ToString(CultureInfo.InvariantCulture),
                DateTimeOffset d => (d.UtcTicks - d.UtcTicks % 10).ToString(CultureInfo.InvariantCulture),
                decimal d => (d / 1.000000000000000000000000000000000m).ToString(CultureInfo.InvariantCulture),
                byte[] bytes => Convert.ToBase64String(bytes),
                IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
                _ => value.ToString()
            }).Append('␟');
        }
        builder.Append('␞');
        hash.AppendData(Encoding.UTF8.GetBytes(builder.ToString()));
    }

    private static string FormatKey(PlannedTable table, object?[] values) =>
        string.Join("/", table.KeyIndexes.Select(k => Convert.ToString(values[k], CultureInfo.InvariantCulture)));

    private sealed record DeferredUpdate(PlannedTable Table, int ColumnIndex, object? Value, object?[] Key);

    private sealed class PlannedTable
    {
        public required string Name { get; init; }
        public required string TargetSql { get; init; }
        public required string SourceSql { get; init; }
        public required IReadOnlyList<IColumn> SourceColumns { get; init; }
        public required IReadOnlyList<IColumn> TargetColumns { get; init; }
        public required IReadOnlyList<RelationalTypeMapping> TargetMappings { get; init; }
        public required IReadOnlyList<int> KeyIndexes { get; init; }
        public List<int> DeferredColumns { get; } = [];

        public string SelectSql(DbContext context, IReadOnlyList<IColumn> columns, string fromSql)
        {
            var helper = context.GetService<ISqlGenerationHelper>();
            var keys = KeyIndexes.Select(k => helper.DelimitIdentifier(columns[k].Name));
            return $"SELECT {string.Join(", ", columns.Select(c => helper.DelimitIdentifier(c.Name)))} FROM {fromSql} ORDER BY {string.Join(", ", keys)}";
        }
    }

    private sealed class TablePlan
    {
        public required IReadOnlyList<PlannedTable> Tables { get; init; }

        /// <summary>
        /// Pairs source and target tables/columns by name and orders the
        /// tables so principals are inserted before dependents. Self
        /// references (document re-upload chains) and nullable foreign keys
        /// that close a cycle are inserted as NULL and restored afterwards.
        /// </summary>
        public static TablePlan Create(DbContext source, DbContext target)
        {
            var helper = target.GetService<ISqlGenerationHelper>();
            var sourceHelper = source.GetService<ISqlGenerationHelper>();
            var sourceTables = source.Model.GetRelationalModel().Tables.ToDictionary(t => t.Name);
            var targetTables = target.Model.GetRelationalModel().Tables.ToList();
            var planned = new Dictionary<string, PlannedTable>();
            foreach (var targetTable in targetTables)
            {
                if (!sourceTables.TryGetValue(targetTable.Name, out var sourceTable))
                    throw new DatabaseCopyException($"Table {targetTable.Name} is missing from the source model.");
                var targetColumns = targetTable.Columns.OrderBy(c => c.Name, StringComparer.Ordinal).ToList();
                var sourceColumns = targetColumns.Select(c => sourceTable.FindColumn(c.Name)
                    ?? throw new DatabaseCopyException($"Column {targetTable.Name}.{c.Name} is missing from the source model.")).ToList();
                var key = targetTable.PrimaryKey
                    ?? throw new DatabaseCopyException($"Table {targetTable.Name} has no primary key.");
                planned[targetTable.Name] = new PlannedTable
                {
                    Name = targetTable.Name,
                    TargetSql = helper.DelimitIdentifier(targetTable.Name, targetTable.Schema),
                    SourceSql = sourceHelper.DelimitIdentifier(sourceTable.Name, sourceTable.Schema),
                    SourceColumns = sourceColumns,
                    TargetColumns = targetColumns,
                    TargetMappings = targetColumns.Select(Mapping).ToList(),
                    KeyIndexes = key.Columns.Select(c => targetColumns.FindIndex(t => t.Name == c.Name)).ToList()
                };
            }

            var dependencies = targetTables.ToDictionary(t => t.Name, t => t.ForeignKeyConstraints.ToList());
            foreach (var table in targetTables)
            foreach (var foreignKey in dependencies[table.Name].Where(fk => fk.PrincipalTable.Name == table.Name).ToList())
            {
                Defer(planned[table.Name], foreignKey);
                dependencies[table.Name].Remove(foreignKey);
            }

            var ordered = new List<PlannedTable>();
            var done = new HashSet<string>();
            while (done.Count < targetTables.Count)
            {
                var ready = targetTables.Where(t => !done.Contains(t.Name)
                        && dependencies[t.Name].All(fk => done.Contains(fk.PrincipalTable.Name)))
                    .OrderBy(t => t.Name, StringComparer.Ordinal).FirstOrDefault();
                if (ready is not null)
                {
                    ordered.Add(planned[ready.Name]);
                    done.Add(ready.Name);
                    continue;
                }
                // Cycle: postpone one nullable reference into a pending table.
                var breakable = targetTables.Where(t => !done.Contains(t.Name))
                    .SelectMany(t => dependencies[t.Name]
                        .Where(fk => !done.Contains(fk.PrincipalTable.Name) && fk.Columns.All(c => c.IsNullable))
                        .Select(fk => (Table: t, ForeignKey: fk)))
                    .OrderBy(x => x.Table.Name, StringComparer.Ordinal).ThenBy(x => x.ForeignKey.Name, StringComparer.Ordinal)
                    .FirstOrDefault();
                if (breakable.ForeignKey is null)
                    throw new DatabaseCopyException("The schema contains a foreign-key cycle without a nullable column; it cannot be copied.");
                Defer(planned[breakable.Table.Name], breakable.ForeignKey);
                dependencies[breakable.Table.Name].Remove(breakable.ForeignKey);
            }
            return new TablePlan { Tables = ordered };
        }

        private static void Defer(PlannedTable table, IForeignKeyConstraint foreignKey)
        {
            foreach (var column in foreignKey.Columns)
            {
                if (!column.IsNullable)
                    throw new DatabaseCopyException($"Self reference {table.Name}.{column.Name} is required; it cannot be copied.");
                var index = table.TargetColumns.ToList().FindIndex(c => c.Name == column.Name);
                if (!table.DeferredColumns.Contains(index)) table.DeferredColumns.Add(index);
            }
        }
    }
}
