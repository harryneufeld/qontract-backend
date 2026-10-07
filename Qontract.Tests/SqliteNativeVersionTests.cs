using Microsoft.Data.Sqlite;

namespace Qontract.Tests;

public class SqliteNativeVersionTests
{
    [Fact]
    public void LoadedNativeEngine_IsAboveKnownAggregateMemoryCorruptionFix()
    {
        // CVE-2025-6965 / GHSA-2m69-gcr7-jv3q affects SQLite before 3.50.2.
        // Check the loaded native engine, rather than only NuGet package names.
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT sqlite_version()";
        var actual = Version.Parse((string)command.ExecuteScalar()!);
        Assert.True(actual >= new Version(3, 50, 2), $"Loaded SQLite {actual} is below the security fix in 3.50.2.");
    }
}
