namespace Qontract.Infrastructure.FileStorage;

/// <summary>
/// The pluggable file storage backends (issue #25). Selected by the
/// "Storage:Provider" configuration value: <c>local</c> (default — disk
/// under <see cref="FileStorageOptions.Path"/>), <c>azure</c> (Azure Blob
/// Storage) or <c>s3</c> (Amazon S3).
/// </summary>
public static class StorageProvider
{
    public const string Local = "local";
    public const string Azure = "azure";
    public const string S3 = "s3";

    /// <summary>
    /// Normalizes a raw "Storage:Provider" value (trim + case-insensitive;
    /// blank/absent = local) and fails fast on unknown values so a typo in
    /// the config is a startup error instead of a silent fallback to disk.
    /// </summary>
    public static string Normalize(string? raw)
    {
        var value = (raw ?? string.Empty).Trim().ToLowerInvariant();
        if (value.Length == 0)
        {
            return Local;
        }

        if (value is not (Local or Azure or S3))
        {
            throw new InvalidOperationException(
                $"Unknown Storage:Provider '{raw}'. Expected 'local' (default), 'azure' or 's3'.");
        }

        return value;
    }
}
