namespace Qontract.Infrastructure.FileStorage;

/// <summary>
/// Settings for the file storage backend, bound from the "Storage"
/// configuration section (issue #34). The maximum file size is defined here
/// only — it used to be hardcoded in both <c>FileStorage</c> and
/// <c>DocumentService</c>.
/// </summary>
public class FileStorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>
    /// The storage backend (issue #25): "local" (default — disk under
    /// <see cref="Path"/>), "azure" (Azure Blob Storage, settings in the
    /// "Storage:Azure" section) or "s3" (Amazon S3, settings in the
    /// "Storage:S3" section). See <see cref="StorageProvider.Normalize"/>.
    /// </summary>
    public string Provider { get; set; } = StorageProvider.Local;

    /// <summary>Root directory that stores all document files (local provider).</summary>
    public string Path { get; set; } = "Storage";

    /// <summary>Maximum allowed size of a single document in bytes (default 50 MB).</summary>
    public int MaxFileSizeBytes { get; set; } = 50 * 1024 * 1024;
}
